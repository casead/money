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
/// USR-004 — Update user profile/role (FR-007 / UC-004).
/// Username immutable. Self-role-change blocked; last-active-Admin demotion blocked.
/// Role change audits before/after values.
/// </summary>
public sealed record UpdateUserCommand(
    long Id,
    string? FullName,
    string? Phone,
    int? RoleId) : IRequest<Result<UserDetailResponse>>, ICommand;

public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Result<UserDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UpdateUserCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<UserDetailResponse>> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct);
        if (user is null)
            return Result<UserDetailResponse>.Failure(ErrorCodes.NotFound, "User ရှာမတွေ့ပါ။");

        var roleBefore = await _db.Roles.FindAsync(user.RoleId);

        // ---- Role change path ----
        var requestedRoleId = request.RoleId;
        var roleChanging = requestedRoleId is not null && requestedRoleId != user.RoleId;
        if (roleChanging)
        {
            // Only SuperAdmin may promote to Admin (USR-004): ShopAdmins manage Staff only.
            var assignmentGuard = UserManagementRules.CheckRoleAssignment(
                _currentUser.RoleId, requestedRoleId!.Value);
            if (assignmentGuard is not null)
                return Result<UserDetailResponse>.Failure(assignmentGuard,
                    "ဆိုင် Admin သည် Role ကို Admin အဖြစ် ပြောင်းလို့မရပါ — SuperAdmin မှသာ လုပ်ဆောင်နိုင်ပါသည်။");

            if (!await _db.Roles.AnyAsync(r => r.Id == requestedRoleId, ct))
                return Result<UserDetailResponse>.Failure(ErrorCodes.ValidationFailed,
                    "Role မှားနေပါသည်။");

            var activeAdminsExcludingTarget = await CountActiveAdminsExcludingAsync(user.Id, ct);
            var guard = UserManagementRules.CheckRoleChange(
                actorId, user.Id,
                demotesLastActiveAdmin: UserManagementRules.IsLastActiveAdmin(
                    activeAdminsExcludingTarget, user.RoleId) &&
                    requestedRoleId != RolePermissionRegistry.AdminRoleId);

            if (guard is not null)
                return Result<UserDetailResponse>.Failure(guard,
                    guard == ErrorCodes.SelfRoleChange
                        ? "ကိုယ်ပိုင် account ရဲ့ Role ကို ပြောင်းလို့မရပါ။"
                        : "နောက်ဆုံးအနေနဲ့ကျန်ရှိတဲ့ Active Admin ကို ဒီလိုပြောင်းလို့မရပါ။");
        }

        // ---- Phone uniqueness when changed ----
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && phone != user.Phone &&
            await _db.Users.AnyAsync(u => u.Phone == phone && u.Id != user.Id, ct))
            return Result<UserDetailResponse>.Failure(ErrorCodes.Duplicate,
                "ဤ Phone နံပါတ်ကို အခြား account မှာ သုံးထားပြီးသားပါ။");

        var before = Serialize(user);

        user.UpdateProfile(request.FullName, phone, actorId, _clock);
        if (roleChanging)
            user.ChangeRole(requestedRoleId!.Value, actorId, _clock);

        await _audit.LogAsync("USER.UPDATE", "User", user.Id.ToString(),
            oldValue: before,
            newValue: Serialize(user),
            ct: ct);

        await _db.SaveChangesAsync(ct);

        var roleAfter = await _db.Roles.FindAsync(user.RoleId);

        return Result<UserDetailResponse>.Success(new UserDetailResponse(
            user.Id, user.Username, user.FullName, user.Phone,
            user.RoleId, roleAfter?.Code ?? "Staff", user.IsActive,
            user.LastLoginAtUtc, user.CreatedAtUtc, user.ModifiedAtUtc));
    }

    private Task<int> CountActiveAdminsExcludingAsync(long userId, CancellationToken ct) =>
        _db.Users.CountAsync(u =>
            u.RoleId == RolePermissionRegistry.AdminRoleId &&
            u.IsActive &&
            u.Id != userId, ct);

    internal static string Serialize(User u) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            u.FullName,
            u.Phone,
            u.RoleId,
            u.IsActive
        });
}
