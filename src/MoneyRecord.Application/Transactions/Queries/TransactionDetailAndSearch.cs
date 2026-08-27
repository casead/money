using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Queries;

/// <summary>Shared: profit/commission fields are Admin-only across ALL endpoints (TC-1200z).</summary>
internal static class ProfitVisibility
{
    public static bool ShowProfit(ICurrentUser user) =>
        user.RoleId == RolePermissionRegistry.AdminRoleId;
}

// ---------- TXN-003 detail ----------

public sealed record GetTransactionQuery(string TxnNo)
    : IRequest<Result<TransactionDetailResponse>>;

public sealed record TransactionDetailResponse(
    string TxnNo,
    string Type,
    string Status,
    long Amount,
    long FeeAmount,
    bool FeeOverridden,
    bool ShowProfitFields,
    long CommissionAmount,
    long ProfitAmount,
    long? CustomerId,
    string CustomerNameSnapshot,
    string CustomerPhoneSnapshot,
    string ProviderCode,
    string AccountName,
    string? Note,
    DateOnly BusinessDate,
    DateTime OccurredAtUtc,
    string CreatedByUserName,
    string? CancelledAtUtc,
    string? ReversalOfTxnNo);

public sealed class GetTransactionQueryHandler
    : IRequestHandler<GetTransactionQuery, Result<TransactionDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetTransactionQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<TransactionDetailResponse>> Handle(GetTransactionQuery request,
        CancellationToken ct)
    {
        var showProfit = ProfitVisibility.ShowProfit(_currentUser);

        var txn = await _db.Transactions.AsNoTracking()
            .Where(t => t.TxnNo == request.TxnNo
                        && t.ShopId == _currentUser.ShopId) // tenant guard (M11)
            .Select(t => new
            {
                t.TxnNo, t.Type, t.Status, t.Amount, t.FeeAmount, t.FeeOverridden,
                t.CommissionAmount, t.CustomerId, t.CustomerNameSnapshot,
                t.CustomerPhoneSnapshot, t.WalletProviderId, t.WalletAccountId,
                t.Note, t.BusinessDate, t.OccurredAtUtc, t.CreatedByUserId,
                t.CancelledAtUtc, t.ReversalOfTxnId
            })
            .FirstOrDefaultAsync(ct);
        if (txn is null)
            return Result<TransactionDetailResponse>.Failure(
                ErrorCodes.NotFound, "Transaction ရှာမတွေ့ပါ။");

        var providerCode = await _db.WalletProviders
            .Where(p => p.Id == txn.WalletProviderId).Select(p => p.Code)
            .FirstAsync(ct);
        var accountName = await _db.WalletAccounts
            .Where(a => a.Id == txn.WalletAccountId).Select(a => a.AccountName)
            .FirstAsync(ct);
        var byUser = await _db.Users
            .Where(u => u.Id == txn.CreatedByUserId).Select(u => u.Username)
            .FirstOrDefaultAsync(ct) ?? $"user:{txn.CreatedByUserId}";
        string? reversalOfTxnNo = null;
        if (txn.ReversalOfTxnId is { } rid)
            reversalOfTxnNo = await _db.Transactions
                .Where(t => t.Id == rid).Select(t => t.TxnNo)
                .FirstOrDefaultAsync(ct);

        return Result<TransactionDetailResponse>.Success(new TransactionDetailResponse(
            txn.TxnNo, txn.Type.ToString(), txn.Status.ToString(),
            txn.Amount, txn.FeeAmount, txn.FeeOverridden,
            showProfit,
            showProfit ? txn.CommissionAmount : 0,
            showProfit ? txn.FeeAmount - txn.CommissionAmount : 0,
            txn.CustomerId, txn.CustomerNameSnapshot, txn.CustomerPhoneSnapshot,
            providerCode, accountName, txn.Note, txn.BusinessDate,
            txn.OccurredAtUtc, byUser,
            txn.CancelledAtUtc?.ToString("O"), reversalOfTxnNo));
    }
}

// ---------- TXN-004 quick search ----------

public sealed record SearchTransactionsQuery(string Term)
    : IRequest<Result<List<TransactionSummaryItem>>>;

public sealed record TransactionSummaryItem(
    string TxnNo,
    string Type,
    string Status,
    long Amount,
    DateTime OccurredAtUtc,
    string CustomerNameSnapshot,
    string CustomerPhoneMasked,
    string ProviderCode);

public sealed class SearchTransactionsQueryValidator : AbstractValidator<SearchTransactionsQuery>
{
    public SearchTransactionsQueryValidator()
    {
        RuleFor(x => x.Term)
            .NotEmpty().MinimumLength(2).WithMessage("အနည်းဆုံး 2 လုံး ရှိရမည်။");
    }
}

public sealed class SearchTransactionsQueryHandler
    : IRequestHandler<SearchTransactionsQuery, Result<List<TransactionSummaryItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public SearchTransactionsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<List<TransactionSummaryItem>>> Handle(
        SearchTransactionsQuery request, CancellationToken ct)
    {
        var term = request.Term.Trim();

        // txnNo exact / phone contains / name contains / amount exact — ≤50 rows.
        var amountMatch = long.TryParse(term, out var amt);
        var phonePrefix = MyanmarPhonePrefix(term);

        // Tenant scope (M11).
        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == _currentUser.ShopId);
        query = phonePrefix is not null
            ? query.Where(t =>
                t.TxnNo == term ||
                t.CustomerPhoneSnapshot.StartsWith(phonePrefix) ||
                t.CustomerNameSnapshot.Contains(term) ||
                (amountMatch && t.Amount == amt))
            : query.Where(t =>
                t.TxnNo == term ||
                t.CustomerNameSnapshot.Contains(term) ||
                (amountMatch && t.Amount == amt));

        var rows = await query
            .OrderByDescending(t => t.OccurredAtUtc)
            .Take(50)
            .Select(t => new
            {
                t.TxnNo, t.Type, t.Status, t.Amount, t.OccurredAtUtc,
                t.CustomerNameSnapshot, t.CustomerPhoneSnapshot,
                ProviderCode = t.WalletProvider.Code
            })
            .ToListAsync(ct);

        return Result<List<TransactionSummaryItem>>.Success(rows.Select(r =>
            new TransactionSummaryItem(
                r.TxnNo, r.Type.ToString(), r.Status.ToString(), r.Amount,
                r.OccurredAtUtc, r.CustomerNameSnapshot,
                Domain.Common.MyanmarPhone.Mask(r.CustomerPhoneSnapshot),
                r.ProviderCode)).ToList());
    }

    private static string? MyanmarPhonePrefix(string term)
    {
        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return null;
        if (digits.StartsWith("0095")) return "0" + digits[4..];
        if (digits.StartsWith("95") && digits.Length >= 5) return "0" + digits[2..];
        return digits.StartsWith("09") ? digits : null;
    }
}
