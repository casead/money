namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>Locked cash row snapshot returned after acquiring UPDLOCK.</summary>
public readonly record struct LockedCashBalance(int Id, long Balance);

/// <summary>Locked wallet account row snapshot returned after acquiring UPDLOCK.</summary>
public readonly record struct LockedWalletBalance(long Id, long Balance);

/// <summary>
/// Balance-row locking port (ARCH §10). Implementations acquire UPDLOCK via raw SQL
/// inside the ambient transaction; callers MUST follow the fixed lock order
/// (cash-in: wallet→cash; cash-out: cash→wallet — BR-035 deadlock rule).
/// </summary>
public interface IBalanceLocker
{
    Task<LockedCashBalance> LockPhysicalCashAsync(CancellationToken ct);

    /// <exception cref="MoneyRecord.Domain.Common.Exceptions.NotFoundException">account missing/deleted.</exception>
    Task<LockedWalletBalance> LockWalletAccountAsync(long walletAccountId, CancellationToken ct);

    /// <summary>
    /// UPDLOCK on a Transactions row inside the ambient transaction (EC-03):
    /// serializes concurrent cancel/reverse; SQL 1222 → LockTimeoutException.
    /// </summary>
    Task<long> LockTransactionRowAsync(long transactionId, CancellationToken ct);
}
