using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Auth.Commands;

/// <summary>
/// AUTH-001 â€” username/password login.
/// Flow (BLUE-010 Â§): lookup â†’ lockout check (domain) â†’ PBKDF2 verify â†’ issue AT+RT pair
/// (RT persisted hashed, device-bound) â†’ audit AUTH.LOGIN. Single transaction via TxBehavior.
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly MoneyRecord.Application.Common.Interfaces.IMoneyRecordDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IRequestContext _requestContext;
    private readonly Application.Common.Interfaces.ITotpService _totp;
    private readonly Microsoft.Extensions.Options.IOptions<AuthTokenOptions> _tokenOptions;

    public LoginCommandHandler(
        IMoneyRecordDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IClock clock,
        IAuditLogger audit,
        IRequestContext requestContext,
        Application.Common.Interfaces.ITotpService totp,
        Microsoft.Extensions.Options.IOptions<AuthTokenOptions> tokenOptions)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _audit = audit;
        _requestContext = requestContext;
        _totp = totp;
        _tokenOptions = tokenOptions;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        // Uniform failure for unknown user vs bad password (no user enumeration).
        if (user is null)
        {
            await _audit.LogAsync("AUTH.LOGIN_FAILED", "User", username,
                newValue: null, ct: ct);
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Username á€žá€­á€¯á€·á€™á€Ÿá€¯á€á€º Password á€™á€¾á€¬á€¸á€”á€±á€•á€«á€žá€Šá€ºá‹");
        }

        // Tenant gate (M11): suspended shops block all member logins.
        if (user.ShopId is not null)
        {
            var shopActive = await _db.Shops
                .AnyAsync(s => s.Id == user.ShopId && s.Status == Shop.ActiveStatus, ct);
            if (!shopActive)
                return Result<LoginResponse>.Failure(ErrorCodes.Forbidden,
                    "á€¤á€†á€­á€¯á€„á€ºá€€á€­á€¯ á€šá€¬á€šá€®á€›á€•á€ºá€”á€¬á€¸á€‘á€¬á€¸á€•á€«á€žá€Šá€º â€” á€á€”á€ºá€†á€±á€¬á€„á€ºá€™á€¾á€¯á€•á€±á€¸á€á€»á€€á€ºá€”á€¾á€„á€·á€º á€†á€€á€ºá€žá€½á€šá€ºá€•á€«á‹");
        }

        bool ok;
        try
        {
            ok = user.VerifyLogin(request.Password, _hasher, _clock);
        }
        catch (AccountLockedException ex)
        {
            await _audit.LogAsync("AUTH.LOGIN_LOCKED", "User", user.Id.ToString(), ct: ct);
            return Result<LoginResponse>.Failure("LOCKED_OUT",
                $"Account á€€á€­á€¯ á€šá€¬á€šá€® á€•á€­á€á€ºá€‘á€¬á€¸á€•á€«á€žá€Šá€º ({ex.LockedUntilUtc:yyyy-MM-dd HH:mm} á€¡á€‘á€­)á‹");
        }

        if (!ok)
        {
            await _audit.LogAsync("AUTH.LOGIN_FAILED", "User", user.Id.ToString(), ct: ct);
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Username á€žá€­á€¯á€·á€™á€Ÿá€¯á€á€º Password á€™á€¾á€¬á€¸á€”á€±á€•á€«á€žá€Šá€ºá‹");
        }

        // ---- TOTP second factor (SEC hardening): required when enrolled ----
        if (user.MfaEnabled)
        {
            var totp = string.IsNullOrWhiteSpace(request.TotpCode)
                ? Result<LoginResponse>.Failure(ErrorCodes.MfaRequired,
                    "2-Step verification code á€‘á€Šá€·á€ºá€•á€«á‹")
                : _totp.Validate(user.MfaSecret ?? string.Empty, request.TotpCode)
                    ? null
                    : Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                        "MFA code á€™á€¾á€¬á€¸á€”á€±á€•á€«á€žá€Šá€ºá‹");

            if (totp is not null)
            {
                await _audit.LogAsync(
                    request.TotpCode is null ? "AUTH.LOGIN_MFA_REQUIRED" : "AUTH.LOGIN_MFA_FAILED",
                    "User", user.Id.ToString(), ct: ct);
                return totp;
            }
        }

        var (accessToken, expiresUtc) = _tokens.CreateAccessToken(user);

        var rawRefresh = _tokens.CreateRefreshToken();
        var refresh = RefreshToken.Issue(
            user.Id,
            ITokenService.HashRefreshToken(rawRefresh),
            _clock.UtcNow,
            lifetimeDays: _tokenOptions.Value.RefreshTokenDays,
            // Canonical device identity = X-Device-Id header (what /auth/refresh validates against);
            // body value only used when the client cannot send headers.
            deviceInfo: _requestContext.DeviceInfo ?? request.DeviceInfo,
            ipAddress: _requestContext.IpAddress);
        _db.RefreshTokens.Add(refresh);

        await _audit.LogAsync("AUTH.LOGIN", "User", user.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                user.Username,
                role = user.Role.Code,
                device = refresh.DeviceInfo
            }), ct: ct);

        await _db.SaveChangesAsync(ct);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            rawRefresh,
            expiresInSeconds(expiresUtc),
            new CurrentUserDto(user.Id, user.Username, user.FullName, user.Role.Code,
                user.ShopId)));
    }

    private static int expiresInSeconds(DateTime expiresUtc) =>
        Math.Max(0, (int)(expiresUtc - DateTime.UtcNow).TotalSeconds);
}
