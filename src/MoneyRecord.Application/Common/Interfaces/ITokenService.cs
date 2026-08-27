namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Token issuance abstraction (ARCH-006 §13). Implemented by Infrastructure TokenService.
/// </summary>
public interface ITokenService
{
    /// <summary>Creates a signed JWT access token; returns raw token + expiry.</summary>
    (string token, DateTime expiresUtc) CreateAccessToken(Domain.Entities.User user);

    /// <summary>Cryptographically random opaque refresh token. Returned raw once, stored hashed.</summary>
    string CreateRefreshToken();

    /// <summary>SHA-256 hex of a refresh token — the value persisted in DB.</summary>
    static string HashRefreshToken(string refreshToken)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(refreshToken)));
}
