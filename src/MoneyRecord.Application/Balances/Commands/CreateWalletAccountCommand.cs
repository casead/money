using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MoneyRecord.UnitTests")]

namespace MoneyRecord.Application.Balances.Commands;

/// <summary>
/// ACC create (setup-wizard extension to API-007 §6 — no ACC-create endpoint was
/// specified, but BR-009 requires opening float at creation; Admin-only).
/// Account + FloatAdjustment('FloatTopUp', 'Opening Float') + ledger entry, atomically.
/// </summary>
public sealed record CreateWalletAccountCommand(
    int ProviderId,
    string AccountName,
    string? AccountNumber,
    long OpeningFloat) : IRequest<Result<WalletAccountResponse>>, ICommand;

public sealed record WalletAccountResponse(
    long Id,
    int ProviderId,
    string ProviderCode,
    string AccountName,
    string? MaskedAccountNumber,
    long CurrentFloatBalance,
    bool IsActive);

public sealed class CreateWalletAccountCommandValidator
    : AbstractValidator<CreateWalletAccountCommand>
{
    public CreateWalletAccountCommandValidator()
    {
        RuleFor(x => x.ProviderId).GreaterThan(0);
        RuleFor(x => x.AccountName)
            .Length(2, 100).WithMessage("Account name သည် 2–100 လုံး ရှိရမည်။");
        RuleFor(x => x.AccountNumber)
            .Length(3, 30).When(x => !string.IsNullOrWhiteSpace(x.AccountNumber));
        RuleFor(x => x.OpeningFloat)
            .GreaterThanOrEqualTo(0).WithMessage("Opening Float သည် အနှုတ် မဖြစ်ရ။");
    }
}

public sealed class CreateWalletAccountCommandHandler
    : IRequestHandler<CreateWalletAccountCommand, Result<WalletAccountResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public CreateWalletAccountCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<WalletAccountResponse>> Handle(
        CreateWalletAccountCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var provider = await _db.WalletProviders
            .FirstOrDefaultAsync(p => p.Id == request.ProviderId && p.IsActive, ct);
        if (provider is null)
            return Result<WalletAccountResponse>.Failure(
                ErrorCodes.NotFound, "Provider ရှာမတွေ့ပါ သို့မဟုတ် ပိတ်ထားပါသည်။");

        if (!string.IsNullOrWhiteSpace(request.AccountNumber) &&
            await _db.WalletAccounts.AnyAsync(a =>
                a.AccountNumber == request.AccountNumber.Trim(), ct))
            return Result<WalletAccountResponse>.Failure(
                ErrorCodes.Duplicate, "ဤ Account Number ဖြင့် account ရှိပြီးသား ဖြစ်နေပါသည်။");

        var account = WalletAccount.Create(
            request.ProviderId, request.AccountName, request.AccountNumber,
            request.OpeningFloat, actorId, _clock, _currentUser.ShopId
                ?? throw new InvalidOperationException("Shop context မရှိပါ။"));
        _db.WalletAccounts.Add(account);
        await _db.SaveChangesAsync(ct);

        // BR-009: opening float lands as an audited FloatTopUp + append-only ledger row.
        long ledgerEntryId = 0;
        if (request.OpeningFloat > 0)
        {
            var adjustment = FloatAdjustment.Create(
                account.Id, LedgerDirection.Increase, request.OpeningFloat,
                "Opening Float", request.OpeningFloat, actorId, _clock);
            _db.FloatAdjustments.Add(adjustment);
            await _db.SaveChangesAsync(ct);

            var entry = WalletLedgerEntry.ForAdjustment(
                account.Id, adjustment.Id, LedgerDirection.Increase,
                request.OpeningFloat, request.OpeningFloat, actorId, _clock);
            _db.WalletLedgerEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
            ledgerEntryId = entry.Id;
        }

        await _audit.LogAsync("ACCOUNT.CREATE", "WalletAccount", account.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                account.Id, provider.Code, account.AccountName,
                openingFloat = request.OpeningFloat
            }), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<WalletAccountResponse>.Success(new WalletAccountResponse(
            account.Id, provider.Id, provider.Code, account.AccountName,
            Mask(account.AccountNumber), account.CurrentFloatBalance, account.IsActive));
    }

    internal static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length <= 4) return number;
        return $"•••{number[^4..]}";
    }
}

// ---- DELETE (soft-delete, Admin-only) ----

public sealed record DeleteWalletAccountCommand(long Id)
    : IRequest<Result>, ICommand;

public sealed class DeleteWalletAccountCommandHandler
    : IRequestHandler<DeleteWalletAccountCommand, Result>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public DeleteWalletAccountCommandHandler(
        IMoneyRecordDbContext db, IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result> Handle(DeleteWalletAccountCommand request, CancellationToken ct)
    {
        var account = await _db.WalletAccounts
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, ct);
        if (account is null)
            return Result.Failure(ErrorCodes.NotFound, "Wallet Account ရှာမတွေ့ပါ။");

        if (account.ShopId != _currentUser.ShopId)
            return Result.Failure(ErrorCodes.Forbidden, "ဤ account ကို ခွင့်ပြုချက် မရှိပါ။");

        if (account.CurrentFloatBalance != 0)
            return Result.Failure(ErrorCodes.ConflictState,
                "Balance ကျန်နေသေးပါသည်။ ငွေလုံးဝရှင်းအောင် ပြီးအောင်လုပ်ပါ။");

        account.Delete(_currentUser.UserId ?? 0, _clock);

        await _audit.LogAsync("ACCOUNT.DELETE", "WalletAccount", account.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                account.AccountName, account.CurrentFloatBalance
            }), ct: ct);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
