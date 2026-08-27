using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Infrastructure.Services;

/// <summary>
/// Production clock. Yangon offset is fixed +06:30 (no DST in Myanmar).
/// </summary>
public sealed class SystemClock : IClock
{
    private static readonly TimeSpan YangonOffset = TimeSpan.FromHours(6.5);

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayYangon => DateOnly.FromDateTime(TimeZoneInfo
        .ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.CreateCustomTimeZone("MMT", YangonOffset, "Myanmar Time", "MMT"))
        .Date);
}
