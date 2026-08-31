using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Balances.Commands;

/// <summary>
/// BAL-003 — audited balance adjustment (T4 boundary, UC-017/018). Admin only
/// (balance.adjust). Scope: cash singleton OR one wallet account.
/// Manual SET mode: when CountedValue is supplied the diff against the locked
/// balance becomes an automatic INCREASE/DECREASE (manual stock-take).
/// Flow: UPDLOCK target row → BR-034 negative guard → adjustment row +
/// paired append-only ledger entry + cache update + audit, atomically (TxBehavior).
/// </summary>
public sealed record AdjustBalanceCommand(
    string Scope,
    long? WalletAccountId,
    string Direction,
    long Amount,
    string? Reason,
    long? CountedValue) : IRequest<Result<AdjustmentResponse>>, ICommand;

public sealed record AdjustmentResponse(
    long AdjustmentId,
    string Scope,
    long NewBalance,
    long LedgerEntryId);

public sealed class AdjustBalanceCommandValidator : AbstractValidator<AdjustBalanceCommand>
{
    private static readonly string[] Scopes = ["cash", "wallet"];

    public AdjustBalanceCommandValidator()
    {
        RuleFor(x => x.Scope)
            .Must(s => Scopes.Contains(s.ToLowerInvariant()))
            .WithMessage("Scope သည် 'cash' သို့မဟုတ် 'wallet' သာ ဖြစ်ရမည်။");
        RuleFor(x => x.WalletAccountId)
            .NotNull().GreaterThan(0)
                .When(x => x.Scope.Equals("wallet", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Wallet scope အတွက် walletAccountId လိုအပ်ပါသည်။");
        // Explicit delta mode requires a valid direction + amount;
        // manual SET mode ignores them and derives both from CountedValue.
        RuleFor(x => x.Direction)
            .Must(d => new[] { "INCREASE", "DECREASE" }.Contains(d.ToUpperInvariant()))
            .WithMessage("Direction သည် INCREASE|DECREASE သာ ဖြစ်ရမည်။")
            .When(x => x.CountedValue is null);
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount သည် ၀ ထက် ကြီးရမည်။")
            .When(x => x.CountedValue is null);
        RuleFor(x => x.CountedValue)
            .GreaterThanOrEqualTo(0).WithMessage("Counted value သည် အနှုတ် မဖြစ်ရပါ။")
            .When(x => x.CountedValue is not null);
        RuleFor(x => x.Reason)
            .Length(10, 300).WithMessage("အကြောင်းပြချက်သည် 10–300 လုံး ရှိရမည်။ (BR-020)")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }

    public static (LedgerDirection Direction, long Amount) ResolveFromCounted(
        long countedValue, long currentBalance) => countedValue >= currentBalance
            ? (LedgerDirection.Increase, countedValue - currentBalance)
            : (LedgerDirection.Decrease, currentBalance - countedValue);
}

public sealed class AdjustBalanceCommandHandler
    : IRequestHandler<AdjustBalanceCommand, Result<AdjustmentResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IBalanceLocker _locker;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public AdjustBalanceCommandHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _locker = locker;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<AdjustmentResponse>> Handle(AdjustBalanceCommand request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;
        var direction = request.Direction.Equals("INCREASE", StringComparison.OrdinalIgnoreCase)
            ? LedgerDirection.Increase
            : LedgerDirection.Decrease;

        if (request.Scope.Equals("cash", StringComparison.OrdinalIgnoreCase))
            return await AdjustCashAsync(request, direction, actorId, ct);

        return await AdjustWalletAsync(request, request.WalletAccountId!.Value,
            direction, actorId, ct);
    }

    /// <summary>
    /// Manual SET mode: derive (direction, delta) from the counted value against
    /// the freshly-locked balance. Zero diff → nothing to adjust.
    /// </summary>
    private static Result<(LedgerDirection Direction, long Amount)> ResolveEffective(
        long? countedValue, LedgerDirection fallbackDirection, long fallbackAmount,
        long lockedBalance)
    {
        if (countedValue is null)
            return Result<(LedgerDirection, long)>.Success((fallbackDirection, fallbackAmount));

        var resolved = AdjustBalanceCommandValidator.ResolveFromCounted(
            countedValue.Value, lockedBalance);
        return resolved.Amount == 0
            ? Result<(LedgerDirection, long)>.Failure(ErrorCodes.ValidationFailed,
                $"ရေတွက်ငွေ {countedValue:N0} Ks သည် လက်ရှိ balance ({lockedBalance:N0} Ks) " +
                "နှင့် တူညီနေပါသည် — ပြင်ဆင်စရာ မရှိပါ။")
            : Result<(LedgerDirection, long)>.Success(resolved);
    }

    private async Task<Result<AdjustmentResponse>> AdjustCashAsync(
        AdjustBalanceCommand request, LedgerDirection direction, long actorId, CancellationToken ct)
    {
        var locked = await _locker.LockPhysicalCashAsync(ct);

        // Clear change tracker to avoid double-tracking with MongoBalanceLocker.
        await _db.ClearTrackedEntitiesAsync(ct);

        var cash = await _db.PhysicalCashAccounts
            .FirstOrDefaultAsync(c => c.Id == locked.Id, ct);
        if (cash is null)
        {
            cash = Domain.Entities.PhysicalCashAccount.CreateForShop(locked.Id, 0, _clock);
            _db.PhysicalCashAccounts.Add(cash);
        }

        // Tenant guard (M11) — cannot adjust another shop's cash.
        if (cash.Id != _currentUser.ShopId)
            return Result<AdjustmentResponse>.Failure(ErrorCodes.Forbidden,
                "အခြားဆိုင်၏ cash balance ကို ချိန်ညှိခွင့် မရှိပါ။");

        var eff = ResolveEffective(request.CountedValue, direction, request.Amount,
            locked.Balance);
        if (!eff.IsSuccess)
            return Result<AdjustmentResponse>.Failure(eff.ErrorCode!, eff.ErrorMessage!);
        var (dir, amount) = eff.Value!;

        var guard = GuardAgainstNegative(dir, amount, locked.Balance);
        if (guard is not null) return guard;

        cash.ApplyAdjustment(dir, amount, actorId, _clock);
        var newBalance = cash.CurrentCashBalance;

        var adjustment = CashAdjustment.Create(
            dir, amount, request.Reason ?? string.Empty, newBalance, actorId, _clock);
        _db.CashAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(ct);

        var entry = CashLedgerEntry.ForAdjustment(
            adjustment.Id, dir, amount, newBalance, actorId, _clock);
        _db.CashLedgerEntries.Add(entry);

        await AuditAsync("cash", adjustment.Id, dir, amount, newBalance, ct);
        await _db.SaveChangesAsync(ct);

        return Result<AdjustmentResponse>.Success(
            new AdjustmentResponse(adjustment.Id, "cash", newBalance, entry.Id));
    }

    private async Task<Result<AdjustmentResponse>> AdjustWalletAsync(
        AdjustBalanceCommand request, long accountId, LedgerDirection direction,
        long actorId, CancellationToken ct)
    {
        var locked = await _locker.LockWalletAccountAsync(accountId, ct);

        // Clear change tracker to avoid double-tracking with MongoBalanceLocker.
        await _db.ClearTrackedEntitiesAsync(ct);

        var account = await _db.WalletAccounts.FirstAsync(a => a.Id == accountId, ct);

        // Tenant guard (M11) — cannot adjust another shop's float.
        if (account.ShopId != _currentUser.ShopId)
            return Result<AdjustmentResponse>.Failure(ErrorCodes.Forbidden,
                "အခြားဆိုင်၏ wallet account ကို ချိန်ညှိခွင့် မရှိပါ။");

        var eff = ResolveEffective(request.CountedValue, direction, request.Amount,
            locked.Balance);
        if (!eff.IsSuccess)
            return Result<AdjustmentResponse>.Failure(eff.ErrorCode!, eff.ErrorMessage!);
        var (dir, amount) = eff.Value!;

        var guard = GuardAgainstNegative(dir, amount, locked.Balance);
        if (guard is not null) return guard;

        account.ApplyAdjustment(dir, amount, actorId, _clock);
        var newBalance = account.CurrentFloatBalance;

        var adjustment = FloatAdjustment.Create(
            accountId, dir, amount, request.Reason ?? string.Empty, newBalance, actorId, _clock);
        _db.FloatAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(ct);

        var entry = WalletLedgerEntry.ForAdjustment(
            accountId, adjustment.Id, dir, amount, newBalance, actorId, _clock);
        _db.WalletLedgerEntries.Add(entry);

        await AuditAsync($"wallet:{accountId}", adjustment.Id, dir, amount,
            newBalance, ct);
        await _db.SaveChangesAsync(ct);

        return Result<AdjustmentResponse>.Success(
            new AdjustmentResponse(adjustment.Id, $"wallet:{accountId}", newBalance, entry.Id));
    }

    /// <summary>BR-034 hard floor — no negative balances through normal operations.</summary>
    private static Result<AdjustmentResponse>? GuardAgainstNegative(
        LedgerDirection direction, long amount, long currentBalance)
    {
        if (direction == LedgerDirection.Decrease && amount > currentBalance)
            return Result<AdjustmentResponse>.Failure(
                ErrorCodes.InsufficientForDecrease,
                $"လက်ရှိ balance ({currentBalance:N0} Ks) ထက် များသော ပမာဏ ဖြတ်လို့ မရပါ။");
        return null;
    }

    private async Task AuditAsync(string scope, long adjustmentId, LedgerDirection direction,
        long amount, long newBalance, CancellationToken ct) =>
        await _audit.LogAsync("BALANCE.ADJUSTMENT", "Balance", scope,
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                adjustmentId,
                direction = direction.ToString(),
                amount,
                newBalance
            }), ct: ct);
}
