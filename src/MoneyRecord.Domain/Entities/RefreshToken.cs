namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Rotating refresh token, stored SHA-256 hashed (ARCH-006 §13).
/// Rotation on every use; reuse of a revoked token = theft signal → revoke all user sessions.
/// </summary>
public class RefreshToken
{
    public long Id { get; private set; }

    public long UserId { get; private set; }

    public User User { get; private set; } = default!;

    /// <summary>SHA-256 hex of the token. Raw token exists only in the HTTP response.</summary>
    public string TokenHash { get; private set; } = default!;

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>NULL until rotated or revoked.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>TokenHash of the replacement token issued during rotation (chain link).</summary>
    public string? ReplacedByTokenHash { get; private set; }

    public string? DeviceInfo { get; private set; }

    public string? IpAddress { get; private set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    private RefreshToken() { } // EF Core

    public static RefreshToken Issue(long userId, string tokenHash, DateTime nowUtc,
        int lifetimeDays, string? deviceInfo, string? ipAddress)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddDays(lifetimeDays),
            DeviceInfo = deviceInfo is null ? null : Truncate(deviceInfo, 200),
            IpAddress = ipAddress
        };
    }

    /// <summary>Rotation: mark consumed and link to successor.</summary>
    public void Rotate(string newTokenHash, DateTime nowUtc)
    {
        RevokedAtUtc = nowUtc;
        ReplacedByTokenHash = newTokenHash;
    }

    /// <summary>Theft signal (AUTH-003): revoke without successor.</summary>
    public void Revoke(DateTime nowUtc) => RevokedAtUtc = nowUtc;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
