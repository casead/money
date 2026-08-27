namespace MoneyRecord.Application.Auth;

/// <summary>
/// Token lifetime settings, bound from configuration section "Jwt"
/// (registered by Infrastructure DI — keeps Application layer config-agnostic).
/// </summary>
public sealed class AuthTokenOptions
{
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}
