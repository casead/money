namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Race-free human-readable txn number source (TXN-YYYY-00001).
/// Backed by a native SQL Server SEQUENCE so concurrent creates never collide.
/// </summary>
public interface ITxnNumberGenerator
{
    Task<long> NextAsync(CancellationToken ct);
}
