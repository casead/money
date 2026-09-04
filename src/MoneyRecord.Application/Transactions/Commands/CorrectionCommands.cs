using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Commands;

// ============================================================================
// TXN-006 — Cancel (BR-021…024): same-day void via compensating ledger entries.
// ============================================================================

/// <summary>TxnNo-keyed; Idempotency-Key header optional but honored when present.</summary>
public sealed record CancelTransactionCommand(string TxnNo, string Reason, Guid? IdempotencyKey)
    : IRequest<Result<CancelTxnResponse>>, ICommand;

public sealed record CancelTxnResponse(
    string CancelledTxnNo,
    string Status,
    BalancesAfter BalancesAfter,
    long CancellationId);

public sealed class CancelTransactionCommandValidator : AbstractValidator<CancelTransactionCommand>
{
    public CancelTransactionCommandValidator()
    {
        RuleFor(x => x.TxnNo).NotEmpty();
        RuleFor(x => x.Reason)
            .Length(5, 300).WithMessage("အကြောင်းပြချက်သည် 5–300 လုံး ရှိရမည်။ (BR-022)");
    }
}

public sealed class CancelTransactionCommandHandler
    : IRequestHandler<CancelTransactionCommand, Result<CancelTxnResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IBalanceLocker _locker;
    private readonly IIdempotencyStore _idempotency;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public CancelTransactionCommandHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IIdempotencyStore idempotency, IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _locker = locker;
        _idempotency = idempotency;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<CancelTxnResponse>> Handle(CancelTransactionCommand request,
        CancellationToken ct)
    {
        // ---- Optional idempotent replay (TXN-006 has no mandatory key);
        //      lease spans the whole operation to serialize concurrent same-key calls ----
        await using var lease =
            request.IdempotencyKey is { } idemKey && idemKey != Guid.Empty
                ? await _idempotency.BeginLeaseAsync(idemKey,
                    CorrectionHash.Compute(request.TxnNo, request.Reason), ct)
                : null;
        if (lease is { Outcome: IdempotencyOutcome.Replay })
            return Result<CancelTxnResponse>.Success(
                JsonSerializer.Deserialize<CancelTxnResponse>(lease.ResponseJson!)!);

        var actorId = _currentUser.UserId ?? 0;

        // ---- Load original by TxnNo ----
        var txn = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TxnNo == request.TxnNo, ct);
        if (txn is null)
            return Result<CancelTxnResponse>.Failure(ErrorCodes.NotFound,
                "Transaction ရှာမတွေ့ပါ။");

        // ---- BR-023 same-BusinessDate routing: cross-day → USE_REVERSAL ----
        if (txn.BusinessDate != _clock.TodayYangon)
            return Result<CancelTxnResponse>.FailureWith(
                ErrorCodes.ConflictState,
                "ယနေ့မဟုတ်သော Transaction ဖြစ်သဖြင့် Reverse flow ကို သုံးပါ။",
                new Dictionary<string, object?> { ["reason"] = "USE_REVERSAL" });

        // ---- EC-03: UPDLOCK the txn row → fresh read under lock → terminal guard ----
        var lockedId = await LockTransactionRowAsync(txn.Id, ct);
        await _db.ClearTrackedEntitiesAsync(ct);
        var current = await _db.Transactions.FirstAsync(t => t.Id == lockedId, ct);
        if (!current.IsCompleted)
            throw new ConflictStateException(
                $"TXN {current.TxnNo} သည် terminal state ({current.Status}) ဖြစ်နေပြီး ပြောင်းလို့မရပါ။");

        // ---- Compensating entries restore BOTH caches (BR-021/BR-024):
        //      cancel(CashIn) = cash↓/float↑ ; cancel(CashOut) = cash↑/float↓.
        //      Fee is undone on the side it originally landed: via Cash → cash −F;
        //      via WalletFloat → wallet −F (the fee had entered the float).
        //      Lock order = global cash→wallet (uniform; see CreateTxnHandlerBase). ----
        var cashIn = current.Type == TransactionType.CashIn;
        var cashDecreases = cashIn;
        var feeOnCash = current.FeeAmount > 0 &&
                        current.FeePaidVia == FeePaidVia.Cash;
        var feeOnWallet = current.FeeAmount > 0 &&
                          current.FeePaidVia == FeePaidVia.WalletFloat;

        LockedCashBalance lockCash;
        LockedWalletBalance lockWallet;
        lockCash = await _locker.LockPhysicalCashAsync(ct);
        lockWallet = await _locker.LockWalletAccountAsync(current.WalletAccountId, ct);

        await _db.ClearTrackedEntitiesAsync(ct);
        var trackedWallet = await _db.WalletAccounts
            .FirstAsync(a => a.Id == current.WalletAccountId, ct);
        var trackedCash = await _db.PhysicalCashAccounts
            .FirstOrDefaultAsync(c => c.Id == lockCash.Id, ct);
        if (trackedCash is null)
        {
            trackedCash = Domain.Entities.PhysicalCashAccount.CreateForShop(lockCash.Id, 0, _clock);
            _db.PhysicalCashAccounts.Add(trackedCash);
        }

        // ---- Sufficiency guards: the decreasing side must cover amount (+fee when
        //      the fee also rides on that same side) ----
        if (cashDecreases && current.Amount + (feeOnCash ? current.FeeAmount : 0)
                > trackedCash.CurrentCashBalance)
            throw new InsufficientCashException(trackedCash.CurrentCashBalance);
        if (!cashDecreases && current.Amount + (feeOnWallet ? current.FeeAmount : 0)
                > trackedWallet.CurrentFloatBalance)
            throw new InsufficientFloatException(trackedWallet.CurrentFloatBalance);

        // ---- Terminal flip + compensating cache/ledger writes — one atomic
        //      business transaction (TxBehavior commit/rollback covers all). ----
        current.MarkCancelled(actorId, request.Reason, _clock.UtcNow);
        _db.Entry(current).Property(t => t.Status).IsModified = true;

        trackedCash.ApplyAdjustment(
            cashDecreases ? LedgerDirection.Decrease : LedgerDirection.Increase,
            current.Amount, actorId, _clock);
        var cashAfterPrincipal = trackedCash.CurrentCashBalance;
        trackedWallet.ApplyAdjustment(
            cashDecreases ? LedgerDirection.Increase : LedgerDirection.Decrease,
            current.Amount, actorId, _clock);
        var floatAfterPrincipal = trackedWallet.CurrentFloatBalance;

        if (feeOnCash)
            trackedCash.ApplyAdjustment(LedgerDirection.Decrease, current.FeeAmount,
                actorId, _clock);
        else if (feeOnWallet)
            trackedWallet.ApplyAdjustment(LedgerDirection.Decrease, current.FeeAmount,
                actorId, _clock);

        var cancellation = TransactionCancellation.Create(
            current.Id, request.Reason, actorId, _clock.UtcNow);
        _db.TransactionCancellations.Add(cancellation);
        _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
            current.Id,
            cashDecreases ? LedgerDirection.Decrease : LedgerDirection.Increase,
            current.Amount, cashAfterPrincipal, actorId, _clock.UtcNow));
        _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
            current.WalletAccountId, current.Id,
            cashDecreases ? LedgerDirection.Increase : LedgerDirection.Decrease,
            current.Amount, floatAfterPrincipal, actorId,
            _clock.UtcNow));

        // Fee compensation entries — appended AFTER the principal entries so the
        // per-account BalanceAfter chain reflects each movement in order.
        if (feeOnCash)
            _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
                current.Id, LedgerDirection.Decrease, current.FeeAmount,
                trackedCash.CurrentCashBalance, actorId, _clock.UtcNow));
        else if (feeOnWallet)
            _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
                current.WalletAccountId, current.Id, LedgerDirection.Decrease,
                current.FeeAmount, trackedWallet.CurrentFloatBalance, actorId,
                _clock.UtcNow));

        await _db.SaveChangesAsync(ct);

        // ---- Audit with before/after (BRL T2) ----
        await _audit.LogAsync("TXN.CANCEL", "Transaction", current.TxnNo,
            oldValue: JsonSerializer.Serialize(new
            {
                status = TransactionStatus.Completed.ToString(),
                cashBefore = lockCash.Balance,
                floatBefore = lockWallet.Balance
            }),
            newValue: JsonSerializer.Serialize(new
            {
                status = current.Status.ToString(),
                reason = request.Reason,
                balancesAfter = new
                {
                    cash = trackedCash.CurrentCashBalance,
                    floatBalance = trackedWallet.CurrentFloatBalance
                }
            }), ct: ct);

        var response = new CancelTxnResponse(
            current.TxnNo, current.Status.ToString(),
            new BalancesAfter(trackedCash.CurrentCashBalance, trackedWallet.CurrentFloatBalance),
            cancellation.Id);

        if (lease is not null)
            await _idempotency.CompleteAsync(lease.Key,
                JsonSerializer.Serialize(response), ct);

        return Result<CancelTxnResponse>.Success(response);
    }

    /// <summary>Raw-SQL UPDLOCK via IBalanceLocker (EC-03); SQL 1222 → LOCK_TIMEOUT.</summary>
    private Task<long> LockTransactionRowAsync(long txnId, CancellationToken ct) =>
        _locker.LockTransactionRowAsync(txnId, ct);
}

internal static class CorrectionHash
{
    public static string Compute(string txnNo, string reason) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new { txnNo, reason = reason.Trim() }))));
}

// ============================================================================
// TXN-007 — Reverse (BR-025…028): cross-day correction via mirror transaction.
// ============================================================================

public sealed record ReverseTransactionCommand(string TxnNo, string Reason,
    DateOnly? EffectiveDate, Guid? IdempotencyKey)
    : IRequest<Result<ReverseTxnResponse>>, ICommand;

public sealed record ReverseTxnResponse(
    string OriginalTxnNo,
    string ReversalTxnNo,
    BalancesAfter BalancesAfter);

public sealed class ReverseTransactionCommandValidator : AbstractValidator<ReverseTransactionCommand>
{
    public ReverseTransactionCommandValidator()
    {
        RuleFor(x => x.TxnNo).NotEmpty();
        RuleFor(x => x.Reason)
            .Length(5, 300).WithMessage("အကြောင်းပြချက်သည် 5–300 လုံး ရှိရမည်။ (BR-026)");
    }
}

public sealed class ReverseTransactionCommandHandler
    : IRequestHandler<ReverseTransactionCommand, Result<ReverseTxnResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IBalanceLocker _locker;
    private readonly IIdempotencyStore _idempotency;
    private readonly ITxnNumberGenerator _txnNumbers;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public ReverseTransactionCommandHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IIdempotencyStore idempotency, ITxnNumberGenerator txnNumbers, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _locker = locker;
        _idempotency = idempotency;
        _txnNumbers = txnNumbers;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<ReverseTxnResponse>> Handle(ReverseTransactionCommand request,
        CancellationToken ct)
    {
        await using var lease =
            request.IdempotencyKey is { } idemKey && idemKey != Guid.Empty
                ? await _idempotency.BeginLeaseAsync(idemKey,
                    CorrectionHash.Compute(request.TxnNo, request.Reason), ct)
                : null;
        if (lease is { Outcome: IdempotencyOutcome.Replay })
            return Result<ReverseTxnResponse>.Success(
                JsonSerializer.Deserialize<ReverseTxnResponse>(lease.ResponseJson!)!);

        var actorId = _currentUser.UserId ?? 0;

        // ---- effectiveDate ≥ original BusinessDate (backdating disallowed, D-default) ----
        var original = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TxnNo == request.TxnNo, ct);
        if (original is null)
            return Result<ReverseTxnResponse>.Failure(ErrorCodes.NotFound,
                "Transaction ရှာမတွေ့ပါ။");
        if (request.EffectiveDate is { } eff && eff < original.BusinessDate)
            return Result<ReverseTxnResponse>.Failure(ErrorCodes.InvalidOperation,
                "effectiveDate သည် မူလ BusinessDate ထက် စောလို့မရပါ။");

        // ---- EC-03: lock txn row → fresh read → terminal guard. This also blocks
        //      reversal-of-reversal (BR-027): REVERSED/CANCELLED are terminal. ----
        var lockedId = await LockTransactionRowAsync(original.Id, ct);
        await _db.ClearTrackedEntitiesAsync(ct);
        var current = await _db.Transactions.FirstAsync(t => t.Id == lockedId, ct);
        if (!current.IsCompleted)
            throw new ConflictStateException(
                $"TXN {current.TxnNo} သည် terminal state ({current.Status}) ဖြစ်နေပြီး ပြောင်းလို့မရပါ။");

        // ---- Mirror txn: OPPOSITE type, same amount/fee snapshots (BR-025/BR-028).
        //      Fees stay non-negative on BOTH rows (CK_Txn_FeeNonNeg); period netting
        //      excludes mirror rows (ReversalOfTxnId ≠ null) alongside their
        //      REVERSED originals so revenue/profit net correctly at report layer. ----
        var cashIn = current.Type == TransactionType.CashIn;
        var mirrorType = cashIn ? TransactionType.CashOut : TransactionType.CashIn;

        // Balance-row locks in the global uniform order (cash → wallet) —
        // mirror-type-dependent ordering would reintroduce the AB-BA cycle.
        LockedCashBalance lockCash;
        LockedWalletBalance lockWallet;
        lockCash = await _locker.LockPhysicalCashAsync(ct);
        lockWallet = await _locker.LockWalletAccountAsync(current.WalletAccountId, ct);

        await _db.ClearTrackedEntitiesAsync(ct);
        var trackedWallet = await _db.WalletAccounts
            .FirstAsync(a => a.Id == current.WalletAccountId, ct);
        var trackedCash = await _db.PhysicalCashAccounts
            .FirstOrDefaultAsync(c => c.Id == lockCash.Id, ct);
        if (trackedCash is null)
        {
            trackedCash = Domain.Entities.PhysicalCashAccount.CreateForShop(lockCash.Id, 0, _clock);
            _db.PhysicalCashAccounts.Add(trackedCash);
        }

        // ---- Sufficiency: mirror undoes the original's movement —
        //      orig=In → mirror Out moves cash ↓ (guard cash; +fee when the original
        //      took its fee on the cash side); orig=Out → mirror In moves float ↓
        //      (guard float). ----
        // feeOnCash = the ORIGINAL collected its fee into the cash pool (via Cash);
        // feeOnWallet = the ORIGINAL collected its fee into the float (via WalletFloat).
        var feeOnCash = current.FeeAmount > 0 &&
                        current.FeePaidVia == FeePaidVia.Cash;
        var feeOnWallet = current.FeeAmount > 0 &&
                          current.FeePaidVia == FeePaidVia.WalletFloat;
        if (cashIn && current.Amount + (feeOnCash ? current.FeeAmount : 0)
                > trackedCash.CurrentCashBalance)
            throw new InsufficientCashException(trackedCash.CurrentCashBalance);
        if (!cashIn && current.Amount + (feeOnWallet ? current.FeeAmount : 0)
                > trackedWallet.CurrentFloatBalance)
            throw new InsufficientFloatException(trackedWallet.CurrentFloatBalance);

        var seq = await _txnNumbers.NextAsync(ct);
        var mirrorNo = $"TXN-{_clock.TodayYangon.Year}-{seq:D5}";

        var mirror = Transaction.Complete(
            mirrorNo, mirrorType, current.Amount,
            feeAmount: current.FeeAmount,
            feeOverridden: false,
            feeRuleId: current.FeeRuleId,
            feePaidVia: current.FeePaidVia,
            feeDeductedFromAmount: false,
            customerId: current.CustomerId,
            customerNameSnapshot: current.CustomerNameSnapshot,
            customerPhoneSnapshot: current.CustomerPhoneSnapshot,
            walletProviderId: current.WalletProviderId,
            walletAccountId: current.WalletAccountId,
            idempotencyKey: Guid.NewGuid(), // internal system op — unique per execution
            note: $"Reversal of {current.TxnNo}: {request.Reason}",
            referenceNo: null, createdByUserId: actorId, clock: _clock,
            shopId: current.ShopId);
        mirror.LinkAsReversalOf(current.Id); // mirror.ReversalOfTxnId → original

        var cashDirection = cashIn ? LedgerDirection.Decrease : LedgerDirection.Increase;
        var walletDirection = cashIn ? LedgerDirection.Increase : LedgerDirection.Decrease;

        trackedCash.ApplyAdjustment(cashDirection, current.Amount, actorId, _clock);
        var cashAfterPrincipal = trackedCash.CurrentCashBalance;
        trackedWallet.ApplyAdjustment(walletDirection, current.Amount, actorId, _clock);
        var floatAfterPrincipal = trackedWallet.CurrentFloatBalance;

        // Undo the original's fee movement as part of the mirror pair:
        // original via Cash added +F to cash → mirror removes it;
        // original via WalletFloat added +F to the float → mirror removes it.
        if (feeOnCash)
            trackedCash.ApplyAdjustment(LedgerDirection.Decrease, current.FeeAmount,
                actorId, _clock);
        else if (feeOnWallet)
            trackedWallet.ApplyAdjustment(LedgerDirection.Decrease, current.FeeAmount,
                actorId, _clock);

        _db.Transactions.Add(mirror);
        await _db.SaveChangesAsync(ct); // materializes mirror.Id for ledger/pair FKs

        _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
            mirror.Id, cashDirection, current.Amount,
            cashAfterPrincipal, actorId, _clock.UtcNow));
        _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
            current.WalletAccountId, mirror.Id, walletDirection, current.Amount,
            floatAfterPrincipal, actorId, _clock.UtcNow));

        // Fee compensation entries — appended AFTER the principal entries so the
        // per-account BalanceAfter chain reflects each movement in order.
        if (feeOnCash)
            _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
                mirror.Id, LedgerDirection.Decrease, current.FeeAmount,
                trackedCash.CurrentCashBalance, actorId, _clock.UtcNow));
        else if (feeOnWallet)
            _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
                current.WalletAccountId, mirror.Id, LedgerDirection.Decrease,
                current.FeeAmount, trackedWallet.CurrentFloatBalance, actorId,
                _clock.UtcNow));

        current.MarkReversed(actorId, request.Reason, _clock.UtcNow, mirrorTxnId: mirror.Id);
        _db.Entry(current).Property(t => t.Status).IsModified = true;
        _db.TransactionReversals.Add(TransactionReversal.Create(
            current.Id, mirror.Id, request.Reason, actorId, _clock.UtcNow));

        await _db.SaveChangesAsync(ct);

        // ---- Audit (BRL T3): one row covering the whole pair ----
        await _audit.LogAsync("TXN.REVERSE", "Transaction", current.TxnNo,
            oldValue: JsonSerializer.Serialize(new
            {
                status = TransactionStatus.Completed.ToString(),
                cashBefore = lockCash.Balance,
                floatBefore = lockWallet.Balance
            }),
            newValue: JsonSerializer.Serialize(new
            {
                status = current.Status.ToString(),
                mirrorTxnNo = mirror.TxnNo,
                reason = request.Reason,
                balancesAfter = new
                {
                    cash = trackedCash.CurrentCashBalance,
                    floatBalance = trackedWallet.CurrentFloatBalance
                }
            }), ct: ct);

        var response = new ReverseTxnResponse(
            current.TxnNo, mirror.TxnNo,
            new BalancesAfter(trackedCash.CurrentCashBalance, trackedWallet.CurrentFloatBalance));

        if (lease is not null)
            await _idempotency.CompleteAsync(lease.Key,
                JsonSerializer.Serialize(response), ct);

        return Result<ReverseTxnResponse>.Success(response);
    }

    private Task<long> LockTransactionRowAsync(long txnId, CancellationToken ct) =>
        _locker.LockTransactionRowAsync(txnId, ct);
}
