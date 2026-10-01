namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Locked cash-row lease (Mongo port of the SQL UPDLOCK snapshot). Dispose
/// releases the process-wide balance mutex — callers MUST dispose (await using)
/// only after the balance write is durable (SaveChangesAsync).
/// </summary>
public sealed class LockedCashBalance : IAsyncDisposable
{
    private readonly Action? _release;
    private int _released;

    public LockedCashBalance(int id, long balance, Action? release = null)
    {
        Id = id;
        Balance = balance;
        _release = release;
    }

    public int Id { get; }

    public long Balance { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _release?.Invoke();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Locked wallet-account lease — same dispose contract as <see cref="LockedCashBalance"/>.</summary>
public sealed class LockedWalletBalance : IAsyncDisposable
{
    private readonly Action? _release;
    private int _released;

    public LockedWalletBalance(long id, long balance, Action? release = null)
    {
        Id = id;
        Balance = balance;
        _release = release;
    }

    public long Id { get; }

    public long Balance { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _release?.Invoke();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Locked transaction-row lease (EC-03) — serializes concurrent cancel/reverse.</summary>
public sealed class LockedTransactionRow : IAsyncDisposable
{
    private readonly Action? _release;
    private int _released;

    public LockedTransactionRow(long id, Action? release = null)
    {
        Id = id;
        _release = release;
    }

    public long Id { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _release?.Invoke();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Balance-row locking port (ARCH §10). The Mongo implementation serializes the
/// read-modify-write critical section per row via a process-wide keyed mutex;
/// callers MUST follow the fixed lock order (cash → wallet — BR-035) and dispose
/// the returned lease after SaveChanges.
/// </summary>
public interface IBalanceLocker
{
    /// <exception cref="MoneyRecord.Domain.Common.Exceptions.LockTimeoutException">wait exceeded the 5s budget (BR-035).</exception>
    Task<LockedCashBalance> LockPhysicalCashAsync(CancellationToken ct);

    /// <exception cref="MoneyRecord.Domain.Common.Exceptions.NotFoundException">account missing/deleted.</exception>
    Task<LockedWalletBalance> LockWalletAccountAsync(long walletAccountId, CancellationToken ct);

    /// <summary>
    /// Mutex on a Transactions row (EC-03): serializes concurrent cancel/reverse;
    /// wait beyond budget → LockTimeoutException (409, retry advised).
    /// </summary>
    Task<LockedTransactionRow> LockTransactionRowAsync(long transactionId, CancellationToken ct);
}
