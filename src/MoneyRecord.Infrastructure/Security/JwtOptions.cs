namespace MoneyRecord.Infrastructure.Security;

/// <summary>JWT + refresh-token settings bound from configuration "Jwt" section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "MoneyRecord";
    public string Audience { get; init; } = "MoneyRecordApp";
    /// <summary>Signing key — supply via user-secrets / env in production. Min 32 chars.</summary>
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 60;
    public int RefreshTokenDays { get; init; } = 90;
}
