using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Notifications.Services;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Commands;

public sealed record SettleCreditCommand(
    string TxnNo,
    string Method,
    long? WalletAccountId,
    long? Amount,
    string? Note,
    bool AllowNegativeBalance = false
) : IRequest<Result<SettleCreditResponse>>;

public sealed record SettleCreditResponse(
    string TxnNo,
    string CreditStatus,
    long SettledAmount,
    string Method,
    long CashBalanceAfter,
    long FloatBalanceAfter);

public sealed class SettleCreditCommandValidator : AbstractValidator<SettleCreditCommand>
{
    public SettleCreditCommandValidator()
    {
        RuleFor(x => x.TxnNo).NotEmpty();
        RuleFor(x => x.Method).Must(m => new[] { "cash", "ewallet", "adjust" }.Contains(m.ToLowerInvariant()))
            .WithMessage("method သည် cash|ewallet|adjust သာ ဖြစ်ရမည်။");
        RuleFor(x => x.WalletAccountId).GreaterThan(0).When(x => x.Method?.ToLowerInvariant() == "ewallet");
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount.HasValue);
    }
}

public sealed class SettleCreditHandler : IRequestHandler<SettleCreditCommand, Result<SettleCreditResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IBalanceLocker _locker;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationService _notificationService;

    public SettleCreditHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IClock clock, ICurrentUser currentUser, INotificationService notificationService)
    {
        _db = db;
        _locker = locker;
        _clock = clock;
        _currentUser = currentUser;
        _notificationService = notificationService;
    }

    public async Task<Result<SettleCreditResponse>> Handle(SettleCreditCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TxnNo == request.TxnNo && t.ShopId == _currentUser.ShopId, ct);

        if (txn is null)
            return Result<SettleCreditResponse>.Failure(ErrorCodes.NotFound, "Transaction ရှာမတွေ့ပါ။");
        if (!txn.IsCredit)
            return Result<SettleCreditResponse>.Failure(ErrorCodes.InvalidOperation, "Credit transaction မဟုတ်ပါ။");
        if (txn.CreditStatus is not (CreditStatus.Pending or CreditStatus.Confirmed))
            return Result<SettleCreditResponse>.Failure(ErrorCodes.InvalidOperation, "Credit settle လုပ်၍မရပါ။");

        var settleAmount = request.Amount ?? txn.Amount;
        if (settleAmount > txn.Amount)
            return Result<SettleCreditResponse>.Failure(ErrorCodes.ValidationFailed, "Settle amount သည် credit amount ထက် မကြီးရပါ။");

        // Balance side is DERIVED from the credit type — the user cannot pick:
        // ပေးရန်ရှိ (creditPaymentMethod='cash')   → creation updated cash,   settle updates WALLET
        // ရရန်ရှိ (creditPaymentMethod='ewallet') → creation updated wallet,  settle updates CASH
        var creditMethod = (txn.CreditPaymentMethod ?? "cash").Trim().ToLowerInvariant();
        var isPayable = creditMethod != "ewallet"; // ပေးရန်ရှိ
        var effectiveMethod = isPayable ? "ewallet" : "cash";
        var cashIn = txn.Type == TransactionType.CashIn;

        await using var cash = await _locker.LockPhysicalCashAsync(ct);
        var trackedCash = await _db.PhysicalCashAccounts.FirstOrDefaultAsync(c => c.Id == cash.Id, ct);

        WalletAccount? trackedWallet = null;
        if (isPayable)
        {
            if (!request.WalletAccountId.HasValue || request.WalletAccountId.Value <= 0)
                return Result<SettleCreditResponse>.Failure(ErrorCodes.ValidationFailed,
                    "E-Wallet ဖြင့်ရှင်းရန် wallet account ရွေးရမည်။");
            trackedWallet = await _db.WalletAccounts
                .FirstOrDefaultAsync(a => a.Id == request.WalletAccountId.Value && !a.IsDeleted, ct);
            if (trackedWallet is null)
                return Result<SettleCreditResponse>.Failure(ErrorCodes.NotFound, "Wallet account ရှာမတွေ့ပါ။");
        }

        if (isPayable)
        {
            // ပေးရန်ရှိ settle → wallet moves (CashIn: out, CashOut: in)
            var walletDir = cashIn ? LedgerDirection.Decrease : LedgerDirection.Increase;
            // Hard floor: never let the wallet go negative silently — the client must
            // explicitly confirm (AllowNegativeBalance) after warning the user.
            if (walletDir == LedgerDirection.Decrease
                && trackedWallet!.CurrentFloatBalance < settleAmount
                && !request.AllowNegativeBalance)
            {
                throw new InsufficientFloatException(trackedWallet.CurrentFloatBalance);
            }
            trackedWallet.ApplyAdjustment(walletDir, settleAmount, actorId, _clock);
            _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
                trackedWallet.Id, txn.Id, walletDir, settleAmount,
                trackedWallet.CurrentFloatBalance, actorId, _clock.UtcNow));
        }
        else
        {
            // ရရန်ရှိ settle → cash moves (CashIn: in, CashOut: out)
            var cashDir = cashIn ? LedgerDirection.Increase : LedgerDirection.Decrease;
            trackedCash!.ApplyAdjustment(cashDir, settleAmount, actorId, _clock);
            _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
                txn.Id, cashDir, settleAmount,
                trackedCash.CurrentCashBalance, actorId, _clock.UtcNow));
        }

        txn.SettleCredit(actorId, _clock.UtcNow);

        await _db.SaveChangesAsync(ct);

        var customerName = txn.CustomerNameSnapshot ?? "Customer";
        var notiTitle = $"{customerName} - {settleAmount:N0} Ks အကြွေးရှင်းပြီ ({effectiveMethod})";
        await _notificationService.NotifyAsync(
            _currentUser.ShopId ?? 0, null,
            NotificationType.CreditSettled,
            "အကြွေးရှင်းပြီးပါပြီ",
            notiTitle,
            JsonSerializer.Serialize(new { txnNo = txn.TxnNo, screen = "credit" }),
            ct);

        long floatAfter = trackedWallet?.CurrentFloatBalance ?? 0;
        if (trackedWallet is null)
        {
            var walletRow = await _db.WalletAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == txn.WalletAccountId && !a.IsDeleted, ct);
            floatAfter = walletRow?.CurrentFloatBalance ?? 0;
        }

        return Result<SettleCreditResponse>.Success(new SettleCreditResponse(
            txn.TxnNo,
            CreditStatus.Settled.ToString(),
            settleAmount,
            effectiveMethod,
            trackedCash.CurrentCashBalance,
            floatAfter));
    }
}