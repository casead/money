using Microsoft.Extensions.Options;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Infrastructure.Security;

/// <summary>
/// Issues access tokens (JWT, 15m) and refresh tokens (opaque 256-bit, stored hashed).
/// ARCH-006 §13: claims sub/name/role/jti.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly Application.Common.Interfaces.IClock _clock;

    public TokenService(IOptions<JwtOptions> options, Application.Common.Interfaces.IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public (string token, DateTime expiresUtc) CreateAccessToken(Domain.Entities.User user)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", user.Id.ToString()),
            new("unique_name", user.Username),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(System.Security.Claims.ClaimTypes.Name, user.Username),
            new(System.Security.Claims.ClaimTypes.Role, user.RoleId.ToString()),
            new("roleId", user.RoleId.ToString()),
            new("fullName", user.FullName),
            new("shopid", user.ShopId?.ToString() ?? ""),
            new("jti", Guid.NewGuid().ToString("N"))
        };

        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(_options.SigningKey)),
            "HS256");

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public string CreateRefreshToken()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
