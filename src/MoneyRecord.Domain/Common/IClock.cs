namespace MoneyRecord.Domain.Common;

/// <summary>
/// Abstraction over the system clock (testability). Yangon BusinessDate derived per BRL-004.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Calendar date in Asia/Yangon timezone (+06:30, no DST).</summary>
    DateOnly TodayYangon { get; }
}
