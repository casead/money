using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Auth.Commands;

/// <summary>SEC — active sessions (devices) of the current user.</summary>
public sealed record ListMySessionsQuery : IRequest<Result<IReadOnlyList<SessionItem>>>;

public sealed record SessionItem(
    long Id, string? DeviceInfo, string? IpAddress,
    DateTime CreatedAtUtc, DateTime ExpiresAtUtc, bool IsCurrentDevice);

public sealed class ListMySessionsQueryHandler
    : IRequestHandler<ListMySessionsQuery, Result<IReadOnlyList<SessionItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;

    public ListMySessionsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser,
        IRequestContext requestContext)
    {
        _db = db;
        _currentUser = currentUser;
        _requestContext = requestContext;
    }

    public async Task<Result<IReadOnlyList<SessionItem>>> Handle(
        ListMySessionsQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<IReadOnlyList<SessionItem>>.Failure(ErrorCodes.Unauthorized,
                "Login ဝင်ပါ။");

        var now = DateTime.UtcNow; // display-only comparison
        var sessions = await _db.RefreshTokens.AsNoTracking()
            .Where(rt => rt.UserId == userId &&
                         rt.RevokedAtUtc == null && rt.ExpiresAtUtc > now)
            .OrderByDescending(rt => rt.CreatedAtUtc)
            .Select(rt => new SessionItem(
                rt.Id, rt.DeviceInfo, rt.IpAddress,
                rt.CreatedAtUtc, rt.ExpiresAtUtc,
                rt.DeviceInfo != null &&
                rt.DeviceInfo == _requestContext.DeviceInfo))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SessionItem>>.Success(sessions);
    }
}

/// <summary>
/// SEC — revoke every active session of the current user.
/// KeepCurrent=true spares sessions bound to the calling device.
/// </summary>
public sealed record RevokeMySessionsCommand(bool KeepCurrent = false)
    : IRequest<Result<Unit>>, ICommand;

public sealed class RevokeMySessionsCommandHandler
    : IRequestHandler<RevokeMySessionsCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly IAuditLogger _audit;

    public RevokeMySessionsCommandHandler(IMoneyRecordDbContext db, ICurrentUser currentUser,
        IClock clock, IRequestContext requestContext, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _requestContext = requestContext;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(RevokeMySessionsCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var now = _clock.UtcNow;
        var active = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > now)
            .ToListAsync(ct);

        var revoked = 0;
        foreach (var token in active)
        {
            if (request.KeepCurrent &&
                !string.IsNullOrEmpty(_requestContext.DeviceInfo) &&
                token.DeviceInfo == _requestContext.DeviceInfo)
                continue;

            token.Revoke(now);
            revoked++;
        }

        await _audit.LogAsync("AUTH.SESSIONS_REVOKED", "User", userId.ToString(),
            newValue: $"count={revoked}; keepCurrent={request.KeepCurrent}", ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
