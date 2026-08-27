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
/// AUTH-003 â€” refresh token rotation.
/// Valid RT â†’ revoke + issue new pair. Reuse of revoked RT â†’ theft signal:
/// revoke ALL user sessions, audit security event, 409 CONFLICT_STATE (TC-300b).
/// </summary>
public sealed record RefreshCommand(string RefreshToken) : IRequest<Result<LoginResponse>>, ICommand;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken á€‘á€Šá€·á€ºá€•á€«á‹");
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
            .Include(rt => rt.User)
            .ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (stored is null)
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Session á€žá€€á€ºá€á€™á€ºá€¸á€€á€¯á€”á€ºá€žá€½á€¬á€¸á€•á€«á€žá€Šá€ºá‹ á€‘á€•á€ºá€á€„á€ºá€•á€«á‹");

        // ---- Theft detection: token already consumed/revoked ----
        if (stored.RevokedAtUtc is not null)
        {
            await RevokeAllUserSessionsAsync(stored.UserId, now, ct);
            await _audit.LogAsync("AUTH.REFRESH_REUSE_DETECTED", "User",
                stored.UserId.ToString(),
                newValue: $"tokenHash={tokenHash[..12]}â€¦; all sessions revoked", ct: ct);
            // Result-based (not exception) so TransactionBehavior COMMITS the revocations â€”
            // TC-300b requires the global revoke to persist. Controller maps to 409 CONFLICT_STATE.
            return Result<LoginResponse>.Failure(ErrorCodes.ConflictState,
                "Refresh token reuse detected. All sessions have been revoked.");
        }

        if (stored.ExpiresAtUtc <= now)
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Session á€žá€€á€ºá€á€™á€ºá€¸á€€á€¯á€”á€ºá€žá€½á€¬á€¸á€•á€«á€žá€Šá€ºá‹ á€‘á€•á€ºá€á€„á€ºá€•á€«á‹");

        // Device binding check (AUTH-003 rule): same device required
        if (stored.DeviceInfo is not null &&
            _requestContext.DeviceInfo is not null &&
            !string.Equals(stored.DeviceInfo, _requestContext.DeviceInfo, StringComparison.Ordinal))
        {
            return Result<LoginResponse>.Failure(ErrorCodes.Unauthorized,
                "Device á€™á€á€°á€Šá€®á€•á€«á‹ á€‘á€•á€ºá€á€„á€ºá€•á€«á‹");
        }

        // User must still be active
        if (!stored.User.IsActive || stored.User.IsDeleted)
            return Result<LoginResponse>.Failure(ErrorCodes.Forbidden,
                "Account á€›á€•á€ºá€á€”á€·á€ºá€‘á€¬á€¸á€•á€«á€žá€Šá€ºá‹");

        // ---- Rotate: consume old, issue new pair ----
        var user = stored.User;
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
            new CurrentUserDto(user.Id, user.Username, user.FullName, user.Role.Code,
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
