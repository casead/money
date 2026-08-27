namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>Pure cache-vs-ledger drift check (DR-08). Unit-testable.</summary>
public static class IntegrityCheck
{
    public const string Mismatch = "MISMATCH";

    public static string? Flag(long cachedBalance, long ledgerSignedSum) =>
        cachedBalance == ledgerSignedSum ? null : Mismatch;

    /// <summary>Signed sum over ledger entries: Increase(+amount), Decrease(−amount).</summary>
    public static long SignedSum(long increaseTotal, long decreaseTotal) =>
        increaseTotal - decreaseTotal;
}
