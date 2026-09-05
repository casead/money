using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;
using FluentValidation;

namespace MoneyRecord.Application.Auth.Commands;

/// <summary>
/// AUTH-003 — refresh token rotation.
/// Valid RT → revoke + issue new pair.
/// Race-window reuse (same-device, &lt;5m) → idempotent replay via chain or
/// graceful "TOKEN_ALREADY_CONSUMED" — never auto-logout.
/// Real theft detection → revoke ALL user sessions + 409 CONFLICT_STATE (TC-300b).
/// </summary>
public sealed record RefreshCommand(string RefreshToken) : IRequest<Result<LoginResponse>>, ICommand;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken ထည့်ပါ။");
    }
}

public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, Result<LoginResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IRequestContext _requestContext;
    private readonly Microsoft.Extensions.Options.IOptions<AuthTokenOptions> _tokenOptions;

    public RefreshCommandHandler(IMoneyRecordDbContext db, ITokenService tokens,
        IClock clock, IAuditLogger audit, IRequestContext requestContext,
        Microsoft.Extensions.Options.IOptions<AuthTokenOptions> tokenOptions)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _audit = audit;
        _requestContext = requestContext;
        _tokenOptions = tokenOptions;
    }

    public async Task<Result<LoginResponse>> Handle(RefreshCommand request, CancellationToken ct)
    {
        var tokenHash = ITokenService.HashRefreshToken(request.RefreshToken);
        var now = _clock.UtcNow;

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (stored is null)
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Session သက်တမ်းကုန်သွားပါပြီ သို့မဟုတ် ပိတ်သိမ်းထားပါသည်။");

        var user = await _db.Users.FindAsync(stored.UserId);

        if (stored.ExpiresAtUtc <= now)
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Session သက်တမ်းကုန်သွားပါပြီ။");

        // Device binding check (AUTH-003 rule): same device required
        if (stored.DeviceInfo is not null &&
            _requestContext.DeviceInfo is not null &&
            !string.Equals(stored.DeviceInfo, _requestContext.DeviceInfo, StringComparison.Ordinal))
        {
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Device မတူညီပါ။");
        }

        // User must still be active
        if (user is null || !user.IsActive || user.IsDeleted)
            return Result<LoginResponse>.Failure(ErrorCodes.Forbidden,
                "Account ရပ်တန့်ထားပါသည်။");

        var role = await _db.Roles.FindAsync(user.RoleId);

        // ---- Token already consumed/revoked → apply race-window grace logic ----
        if (stored.RevokedAtUtc is not null)
        {
            bool sameDevice =
                string.Equals(stored.DeviceInfo, _requestContext.DeviceInfo, StringComparison.Ordinal);

            TimeSpan sinceRevoked = now - stored.RevokedAtUtc.Value;
            bool withinGraceWindow = sinceRevoked <= TimeSpan.FromMinutes(5);

            if (sameDevice && withinGraceWindow)
            {
                // Best path: the successor token was already issued & still active —
                // replay it against the *same raw refresh* the caller holds, so the
                // client-side storage stays consistent (no double-rotation).
                if (stored.ReplacedByTokenHash is not null)
                {
                    var replacement = await _db.RefreshTokens
                        .FirstOrDefaultAsync(rt => rt.TokenHash == stored.ReplacedByTokenHash, ct);

                    if (replacement is not null && replacement.RevokedAtUtc is null)
                    {
                        var (reissueAccess, reissueExpires) = _tokens.CreateAccessToken(user!);
                        await _audit.LogAsync("AUTH.REFRESH_IDEMPOTENT_REPLAY", "User",
                            stored.UserId.ToString(),
                            newValue: $"original={tokenHash[..12]}...; replay via replacement",
                            ct: ct);
                        return Result<LoginResponse>.Success(new LoginResponse(
                            reissueAccess,
                            request.RefreshToken,
                            Math.Max(0, (int)(reissueExpires - DateTime.UtcNow).TotalSeconds),
                            new CurrentUserDto(user!.Id, user.Username, user.FullName,
                                role?.Code ?? "Staff", user.ShopId)));
                    }
                }

                // Fallback graceful 401: instructs the client NOT to wipe tokens /
                // NOT to force-logout, just surface a transient retry banner.
                await _audit.LogAsync("AUTH.REFRESH_RACE_RETRY", "User",
                    stored.UserId.ToString(),
                    newValue: $"hash={tokenHash[..12]}...; age={sinceRevoked.TotalSeconds:F0}s",
                    ct: ct);
                return Result<LoginResponse>.Failure(ErrorCodes.TokenAlreadyConsumed,
                    "Token သုံးပြီးသားဖြစ်သည်။ ခဏနေပြီး ပြန်ကြိုးစားပါ။");
            }

            // Outside grace window or different device → real-theft path (TC-300b).
            await RevokeAllUserSessionsAsync(stored.UserId, now, ct);
            await _audit.LogAsync("AUTH.REFRESH_REUSE_DETECTED", "User",
                stored.UserId.ToString(),
                newValue: $"tokenHash={tokenHash[..12]}...; deviceMatch={sameDevice}; age={sinceRevoked.TotalSeconds:F0}s; all sessions revoked",
                ct: ct);
            return Result<LoginResponse>.Failure(ErrorCodes.ConflictState,
                "Refresh token reuse detected. All sessions have been revoked.");
        }

        // ---- Rotate: consume old, issue new pair ----
        var rawNew = _tokens.CreateRefreshToken();
        var newHash = ITokenService.HashRefreshToken(rawNew);

        stored.Rotate(newHash, now);

        _db.RefreshTokens.Add(RefreshToken.Issue(
            user.Id, newHash, now, lifetimeDays: _tokenOptions.Value.RefreshTokenDays,
            deviceInfo: stored.DeviceInfo, ipAddress: _requestContext.IpAddress));

        var (accessToken, expiresUtc) = _tokens.CreateAccessToken(user);

        await _audit.LogAsync("AUTH.REFRESH", "User", user.Id.ToString(), ct: ct);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            rawNew,
            Math.Max(0, (int)(expiresUtc - DateTime.UtcNow).TotalSeconds),
            new CurrentUserDto(user.Id, user.Username, user.FullName, role?.Code ?? "Staff",
                user.ShopId)));
    }

    private async Task RevokeAllUserSessionsAsync(long userId, DateTime now, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in active)
            token.Revoke(now);
    }
}
