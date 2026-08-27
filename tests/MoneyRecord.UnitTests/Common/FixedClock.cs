using MoneyRecord.Domain.Common;

namespace MoneyRecord.UnitTests.Common;

/// <summary>Deterministic clock for reproducible time-sensitive rules (lockout, expiry).</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; private set; } = utcNow;

    public DateOnly TodayYangon => DateOnly.FromDateTime(
        UtcNow.AddHours(6).AddMinutes(30));

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Scripted password verifier — no real KDF cost inside unit tests.</summary>
public sealed class StubPasswordHasher(string? validPassword) : MoneyRecord.Domain.Entities.IPasswordHasher
{
    public string Hash(string password) => $"stub::{password}";

    public bool Verify(string password, string storedHash) => password == validPassword;
}
