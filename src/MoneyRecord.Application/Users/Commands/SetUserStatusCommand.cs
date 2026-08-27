using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Users.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Users.Commands;

/// <summary>
/// USR-005 — Activate/Deactivate (FR-007 / UC-004, BC-02 soft-lock).
/// Deactivate = login blocked + ALL refresh tokens revoked immediately (SEC-007).
/// Self-deactivate and last-active-Admin deactivation blocked (403). Never hard-delete.
/// </summary>
public sealed record SetUserStatusCommand(long Id, bool IsActive)
    : IRequest<Result<UserStatusResponse>>, ICommand;

public sealed class SetUserStatusCommandHandler : IRequestHandler<SetUserStatusCommand, Result<UserStatusResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public SetUserStatusCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<UserStatusResponse>> Handle(SetUserStatusCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct);
        if (user is null)
            return Result<UserStatusResponse>.Failure(ErrorCodes.NotFound, "User ရှာမတွေ့ပါ။");

        var deactivating = !request.IsActive;

        if (deactivating && user.IsActive)
        {
            var activeAdminsExcludingTarget = await _db.Users.CountAsync(u =>
                u.RoleId == RolePermissionRegistry.AdminRoleId &&
                u.IsActive &&
                u.Id != user.Id, ct);

            var guard = UserManagementRules.CheckStatusChange(
                actorId, user.Id,
                deactivating: true,
                targetIsLastActiveAdmin: UserManagementRules.IsLastActiveAdmin(
                    activeAdminsExcludingTarget, user.RoleId));

            if (guard is not null)
                return Result<UserStatusResponse>.Failure(guard,
                    guard == ErrorCodes.SelfDeactivate
                        ? "ကိုယ်ပိုင် account ကို Deactivate လုပ်လို့မရပါ။"
                        : "နောက်ဆုံးအနေနဲ့ကျန်ရှိတဲ့ Active Admin ကို Deactivate လုပ်လို့မရပါ။");
        }

        if (user.IsActive == request.IsActive)
            return Result<UserStatusResponse>.Success(new UserStatusResponse(user.Id, user.IsActive)); // idempotent

        var before = System.Text.Json.JsonSerializer.Serialize(new { user.IsActive });

        if (deactivating)
        {
            user.Deactivate(actorId, _clock);
            await RevokeAllSessionsAsync(user.Id, ct);
        }
        else
        {
            user.Reactivate(actorId, _clock);
        }

        await _audit.LogAsync("USER.STATUS_CHANGE", "User", user.Id.ToString(),
            oldValue: before,
            newValue: System.Text.Json.JsonSerializer.Serialize(new { user.IsActive }),
            ct: ct);

        await _db.SaveChangesAsync(ct);

        return Result<UserStatusResponse>.Success(new UserStatusResponse(user.Id, user.IsActive));
    }

    /// <summary>SEC-007 / TC-300d: existing tokens must die the instant deactivation lands.</summary>
    private async Task RevokeAllSessionsAsync(long userId, CancellationToken ct)
    {
        var nowUtc = _clock.UtcNow;
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
            token.Revoke(nowUtc);
    }
}
