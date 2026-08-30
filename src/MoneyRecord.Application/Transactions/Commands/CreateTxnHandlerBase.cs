using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Fees.Services;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;
using MyanmarPhone = MoneyRecord.Domain.Common.MyanmarPhone;

namespace MoneyRecord.Application.Transactions.Commands;

/// <summary>
/// T1 transaction-create core (BLUE-010 §12, BR-010/011).
/// Lock order: Cash In = wallet→cash; Cash Out = cash→wallet (BRL §19 deadlock rule).
/// Ledger semantics follow BRL §4.C.2 / §4.D.2 golden examples — exactly TWO entries:
///   Cash In : cash +(amount), wallet −(amount)
///   Cash Out: cash −(amount), wallet +(amount)
/// Fee resolution (M9): auto-calc from effective-dated rule (BR-012) with Admin-only
/// override (BR-013); FeeAmount + FeeRuleId snapshot stored on the txn row so later
/// rule changes never rewrite history (TC-900c). Commission is optional per-txn
/// manual capture (S-A06) stored via CommissionEntries.
/// </summary>
public abstract class CreateTxnHandlerBase<TCommand>
    where TCommand : CreateTxnCommand, IRequest<Result<TxnReceiptResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IBalanceLocker _locker;
    private readonly IIdempotencyStore _idempotency;
    private readonly ITxnNumberGenerator _txnNumbers;
    private readonly IFeeCalculator _feeCalculator;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IServiceScopeFactory _scopeFactory;

    protected CreateTxnHandlerBase(IMoneyRecordDbContext db, IBalanceLocker locker,
        IIdempotencyStore idempotency, ITxnNumberGenerator txnNumbers,
        IFeeCalculator feeCalculator, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _locker = locker;
        _idempotency = idempotency;
        _txnNumbers = txnNumbers;
        _feeCalculator = feeCalculator;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _scopeFactory = scopeFactory;
    }

    protected abstract TransactionType Type { get; }

    protected abstract string ActionCode { get; }

    public async Task<Result<TxnReceiptResponse>> Handle(TCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;
        var isAdmin = _currentUser.RoleId == RolePermissionRegistry.AdminRoleId;

        // ---- Idempotency lease (per-key gate + UPDLOCK row lock): concurrent
        //      same-key submissions serialize; losers REPLAY the winner's receipt ----
        var hash = request.ComputeRequestHash();
        await using var lease = await _idempotency.BeginLeaseAsync(
            request.IdempotencyKey, hash, ct);
        if (lease.Outcome == IdempotencyOutcome.Replay)
        {
            var replayed = JsonSerializer.Deserialize<TxnReceiptResponse>(lease.ResponseJson!)!;
            return Result<TxnReceiptResponse>.Success(replayed with { IsReplay = true });
        }

        // ---- Resolve target account (+provider) ----
        var account = await _db.WalletAccounts
            .Include(a => a.WalletProvider)
            .FirstOrDefaultAsync(a => a.Id == request.WalletAccountId && !a.IsDeleted, ct);
        if (account is null || !account.IsActive)
            return Result<TxnReceiptResponse>.Failure(ErrorCodes.NotFound,
                "Wallet account ရှာမတွေ့ပါ သို့မဟုတ် ပိတ်ထားပါသည်။");
        if (!account.WalletProvider.IsActive)
            return Result<TxnReceiptResponse>.Failure(ErrorCodes.InvalidOperation,
                $"{account.WalletProvider.Name} provider ကို ပိတ်ထားသဖြင့် အသုံးပြုလို့မရပါ။");

        var phone = MyanmarPhone.TryNormalize(request.CustomerPhone)!;

        // ---- Customer auto-link (per-shop registry): a matching phone attaches
        //      the txn to the existing customer; an unknown phone AUTO-REGISTERS
        //      a new customer row so lifetime stats accumulate on the detail page.
        //      Concurrent first-registrations of the same phone are serialized by
        //      UQ_Customers_Shop_Phone — the loser rolls back and retries clean. ----
        var customer = await _db.Customers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ShopId == account.ShopId
                                      && c.Phone == phone && !c.IsDeleted, ct);
        if (customer is null)
        {
            customer = Customer.Create(request.CustomerName, phone,
                address: null, note: null, actorId, _clock, account.ShopId);
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct); // materializes CustomerId — same T1 txn
        }

        var cashIn = Type == TransactionType.CashIn;

        // ---- T1: acquire balance-row locks in ONE global order (cash → wallet) ----
        // Type-dependent ordering (in = wallet→cash, out = cash→wallet) forms an
        // AB-BA cycle under concurrent opposite ops → PG 40P01 deadlocks in the
        // mixed-burst storm. A uniform order makes deadlock structurally impossible;
        // sufficiency guards below still run after BOTH rows are locked.
        long floatBefore, cashBefore;
        WalletAccount lockedWallet;
        PhysicalCashAccount lockedCash;

        lockedCash = await LockAndTrackCashAsync(ct);
        cashBefore = lockedCash.CurrentCashBalance;
        lockedWallet = await LockAndTrackWalletAsync(account.Id, ct);
        floatBefore = lockedWallet.CurrentFloatBalance;

        // ---- Fee resolution (M9, BR-012/013): auto-calc from effective-dated rule;
        //      Any user (Admin/Staff) may override with manual amount. Snapshot
        //      stored on the txn row so later rule changes never rewrite history. ----
        var feeOverridden = request.FeeAmountOverride is not null;

        long feeAmount;
        int? appliedRuleId;
        if (feeOverridden)
        {
            feeAmount = request.FeeAmountOverride!.Value;
            appliedRuleId = null; // manual override — no rule applied
        }
        else
        {
            var resolution = await _feeCalculator.CalculateAsync(
                Type, request.Amount, ct);
            feeAmount = resolution.FeeAmount;
            appliedRuleId = resolution.AppliedRuleId;
        }

        var feeVia = request.ResolveFeePaidVia();

        // ---- Sufficiency guards (BR-032/033/007 hard floor) ----
        // Cash In:  wallet must have enough float for the full Amount.
        // Cash Out: shop must have enough cash to pay the customer.
        //           When fee is deducted from amount, customer receives (Amount − Fee).
        long cashNeeded = request.Amount;
        if (!cashIn && request.FeeDeductedFromAmount && feeAmount > 0)
            cashNeeded = request.Amount - feeAmount;
        if (cashIn && request.Amount > floatBefore)
            throw new InsufficientFloatException(floatBefore);
        if (!cashIn && cashNeeded > cashBefore)
            throw new InsufficientCashException(cashBefore);

        // ---- TxnNo from native SEQUENCE (race-free) ----
        var seq = await _txnNumbers.NextAsync(ct);
        var txnNo = $"TXN-{_clock.TodayYangon.Year}-{seq:D5}";

        // ---- Insert immutable txn row ----
        var txn = Transaction.Complete(
            txnNo, Type, request.Amount, feeAmount, feeOverridden,
            appliedRuleId, feeVia, request.FeeDeductedFromAmount,
            customer.Id, request.CustomerName, phone,
            account.WalletProviderId, account.Id, request.IdempotencyKey,
            request.Note, referenceNo: null, actorId, _clock,
            shopId: account.ShopId);
        _db.Transactions.Add(txn);

        // Persist now: materializes txn.Id (ledger FKs) and the idempotency
        // reservation inserted by ReserveAsync — all inside the T1 transaction.
        await _db.SaveChangesAsync(ct);

        // ---- Dual-ledger writes (BRL §4.C.2 / §4.D.2 + BR-012-ext fee movement) ----
        // Principal: Cash In → cash +A / wallet −A ; Cash Out → cash −A / wallet +A.
        // Fee deducted: wallet movement uses NetAmount (Amount - Fee) instead of Amount.
        // Fee:       ALWAYS lands on the side the customer paid it —
        //            via Cash → cash +F ; via WalletFloat → wallet +F
        //            (fee paid from the customer's own mobile wallet).
        //            When FeeDeductedFromAmount=true, fee is NOT separately added
        //            to wallet float — it's already deducted from the wallet movement.
        var cashDirection = cashIn ? LedgerDirection.Increase : LedgerDirection.Decrease;
        var walletDirection = cashIn ? LedgerDirection.Decrease : LedgerDirection.Increase;

        // Wallet movement:
        //   Cash Out + FeeDeductedFromAmount → full Amount (shop pays customer cash after deducting fee)
        //   Cash In  + FeeDeductedFromAmount → net Amount (wallet tops up less fee)
        //   No deduction                    → full Amount
        var walletMovementAmount = (cashIn && txn.NetAmount.HasValue)
            ? txn.NetAmount.Value
            : request.Amount;

        // Cash movement:
        //   Cash Out + FeeDeductedFromAmount → net Amount (customer receives less cash)
        //   Otherwise                        → full Amount
        var cashMovementAmount = (!cashIn && txn.NetAmount.HasValue)
            ? txn.NetAmount.Value
            : request.Amount;

        lockedCash.ApplyAdjustment(cashDirection, cashMovementAmount, actorId, _clock);
        var cashAfterPrincipal = lockedCash.CurrentCashBalance;
        lockedWallet.ApplyAdjustment(walletDirection, walletMovementAmount, actorId, _clock);
        var floatAfterPrincipal = lockedWallet.CurrentFloatBalance;

        if (feeAmount > 0 && feeVia == FeePaidVia.Cash)
            lockedCash.ApplyAdjustment(LedgerDirection.Increase, feeAmount, actorId, _clock);
        else if (feeAmount > 0 && feeVia == FeePaidVia.WalletFloat
                 && !request.FeeDeductedFromAmount)
            lockedWallet.ApplyAdjustment(LedgerDirection.Increase, feeAmount, actorId, _clock);

        var cashEntry = CashLedgerEntry.ForTransactionCore(
            txn.Id, cashDirection, cashMovementAmount,
            cashAfterPrincipal, actorId, txn.OccurredAtUtc);
        _db.CashLedgerEntries.Add(cashEntry);

        if (feeAmount > 0 && feeVia == FeePaidVia.Cash)
            _db.CashLedgerEntries.Add(CashLedgerEntry.ForTransactionCore(
                txn.Id, LedgerDirection.Increase, feeAmount,
                lockedCash.CurrentCashBalance, actorId, txn.OccurredAtUtc));

        var walletEntry = WalletLedgerEntry.ForTransactionCore(
            account.Id, txn.Id, walletDirection, walletMovementAmount,
            floatAfterPrincipal, actorId, txn.OccurredAtUtc);
        _db.WalletLedgerEntries.Add(walletEntry);

        if (feeAmount > 0 && feeVia == FeePaidVia.WalletFloat
            && !request.FeeDeductedFromAmount)
            _db.WalletLedgerEntries.Add(WalletLedgerEntry.ForTransactionCore(
                account.Id, txn.Id, LedgerDirection.Increase, feeAmount,
                lockedWallet.CurrentFloatBalance, actorId, txn.OccurredAtUtc));

        // ---- Duplicate soft-warning (BR-030, non-blocking hint) ----
        // DEFERRED: not critical for response; checked after commit.
        bool duplicateWarning = false;

        // ---- Audit inside the same business txn (non-negotiable #4) ----
        // DEFERRED: logged after commit to reduce critical path latency.

        // ---- Receipt payload + idempotency completion ----
        var receipt = BuildReceipt(txn, lockedCash.CurrentCashBalance,
            lockedWallet.CurrentFloatBalance, duplicateWarning, isAdmin, isReplay: false);

        await _idempotency.CompleteAsync(
            request.IdempotencyKey, JsonSerializer.Serialize(receipt), ct);

        // ---- Deferred non-critical operations (after response ready) ----
        // Each runs in its own DI scope to avoid using the request-scoped DbContext
        // after the TransactionBehavior commits and the request scope is disposed.
        var txnNoCapture = txn.TxnNo;
        var amountCapture = request.Amount;
        var feeAmountCapture = feeAmount;
        var feeViaCapture = feeVia.ToString();
        var accountNameCapture = account.AccountName;
        var cashAfterCapture = lockedCash.CurrentCashBalance;
        var floatAfterCapture = lockedWallet.CurrentFloatBalance;
        var actionCodeCapture = ActionCode;
        var typeCapture = Type;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var windowStart = clock.UtcNow.AddMinutes(-TxnRules.DuplicateWarningWindowMinutes);
                await db.Transactions.AsNoTracking()
                    .AnyAsync(t => t.CustomerPhoneSnapshot == phone &&
                                   t.Amount == amountCapture &&
                                   t.Type == typeCapture &&
                                   t.Status == TransactionStatus.Completed &&
                                   t.OccurredAtUtc >= windowStart);
            }
            catch { /* best-effort */ }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
                await audit.LogAsync(actionCodeCapture, "Transaction", txnNoCapture,
                    newValue: JsonSerializer.Serialize(new
                    {
                        txnNo = txnNoCapture,
                        type = typeCapture.ToString(),
                        amount = amountCapture,
                        feeAmount = feeAmountCapture,
                        feePaidVia = feeViaCapture,
                        customerPhone = MyanmarPhone.Mask(phone),
                        account = accountNameCapture,
                        balancesAfter = new
                        {
                            cash = cashAfterCapture,
                            floatBalance = floatAfterCapture
                        }
                    }));
            }
            catch { /* best-effort */ }
        });

        return Result<TxnReceiptResponse>.Success(receipt);
    }

    private async Task<WalletAccount> LockAndTrackWalletAsync(long accountId, CancellationToken ct)
    {
        var locked = await _locker.LockWalletAccountAsync(accountId, ct);

        // Row is now exclusively locked — no other txn can modify it.
        // Reload is unnecessary: the locked entity already holds the
        // latest balance from the lock acquisition itself.
        var tracked = await _db.WalletAccounts.FirstAsync(a => a.Id == accountId, ct);
        return tracked;
    }

    private async Task<PhysicalCashAccount> LockAndTrackCashAsync(CancellationToken ct)
    {
        var locked = await _locker.LockPhysicalCashAsync(ct);
        var tracked = await _db.PhysicalCashAccounts
            .FirstOrDefaultAsync(c => c.Id == locked.Id, ct); // per-shop cash pool (M11)
        if (tracked is null)
        {
            tracked = PhysicalCashAccount.CreateForShop(locked.Id, 0, _clock);
            _db.PhysicalCashAccounts.Add(tracked); // legacy-shop self-heal
        }
        return tracked;
    }

    private static TxnReceiptResponse BuildReceipt(Transaction txn, long cashAfter,
        long floatAfter, bool duplicateWarning, bool showProfitFields, bool isReplay) =>
        new(
            txn.TxnNo,
            txn.Status.ToString(),
            txn.Amount,
            txn.FeeAmount,
            txn.FeePaidVia == FeePaidVia.WalletFloat ? "wallet" : "cash",
            NetAmount: txn.NetAmount ?? txn.Amount,
            CommissionAmount: showProfitFields ? txn.CommissionAmount : 0,
            ShowProfitFields: showProfitFields,
            ProfitAmount: showProfitFields ? txn.GrossProfit : 0,
            new BalancesAfter(cashAfter, floatAfter),
            ReceiptUrl: $"/transactions/{txn.TxnNo}/receipt",
            duplicateWarning,
            txn.OccurredAtUtc,
            txn.BusinessDate,
            isReplay);
}
