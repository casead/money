namespace MoneyRecord.Application.Auth;

/// <summary>
/// Token lifetime settings, bound from configuration section "Jwt"
/// (registered by Infrastructure DI — keeps Application layer config-agnostic).
/// </summary>
public sealed class AuthTokenOptions
{
    public int AccessTokenMinutes { get; set; } = 525600; // 365 days

    public int RefreshTokenDays { get; set; } = 365;
}
