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
/// USR-002 — Create user (FR-006 / UC-003). Admin-only (enforced by policy).
/// Duplicate username/phone → 409 DUPLICATE; password hashed PBKDF2; audit USER.CREATE.
/// ShopId: SuperAdmin may place the user in a shop (onboarding); shop admins always
/// create within their own shop.
/// </summary>
public sealed record CreateUserCommand(
    string Username,
    string Password,
    string FullName,
    string? Phone,
    int RoleId,
    long? ShopId = null) : IRequest<Result<UserDetailResponse>>, ICommand;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<UserDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public CreateUserCommandHandler(IMoneyRecordDbContext db, IPasswordHasher hasher,
        IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<UserDetailResponse>> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        // Only SuperAdmin may grant the Admin role (USR-002): ShopAdmins create Staff only.
        var assignmentGuard = UserManagementRules.CheckRoleAssignment(_currentUser.RoleId, request.RoleId);
        if (assignmentGuard is not null)
            return Result<UserDetailResponse>.Failure(assignmentGuard,
                "ဆိုင် Admin သည် Admin account ဖန်တီးခွင့် မရှိပါ — Staff account သာ ဖန်တီးနိုင်ပါသည်။");

        var username = request.Username.Trim();

        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            return Result<UserDetailResponse>.Failure(ErrorCodes.Duplicate,
                $"Username '{username}' ကို အခြား account မှာ သုံးထားပြီးသားပါ။");

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && await _db.Users.AnyAsync(u => u.Phone == phone, ct))
            return Result<UserDetailResponse>.Failure(ErrorCodes.Duplicate,
                "ဤ Phone နံပါတ်ကို အခြား account မှာ သုံးထားပြီးသားပါ။");

        var roleCode = await _db.Roles
            .Where(r => r.Id == request.RoleId)
            .Select(r => r.Code)
            .FirstOrDefaultAsync(ct);
        if (roleCode is null)
            return Result<UserDetailResponse>.Failure(ErrorCodes.ValidationFailed,
                "Role မှားနေပါသည်။");

        // Tenancy: shop admins are locked to their own shop; only the SuperAdmin
        // may target an arbitrary (existing, active) shop when onboarding.
        long? shopId = _currentUser.ShopId;
        if (_currentUser.RoleId == RolePermissionRegistry.SuperAdminRoleId &&
            request.ShopId is { } requestedShopId)
        {
            if (!await _db.Shops.AnyAsync(
                    s => s.Id == requestedShopId && s.Status == Shop.ActiveStatus, ct))
                return Result<UserDetailResponse>.Failure(ErrorCodes.ValidationFailed,
                    "ဆိုင် ရှာမတွေ့ပါ သို့မဟုတ် ရပ်နားထားပါသည်။");
            shopId = requestedShopId;
        }
        else if (_currentUser.RoleId != RolePermissionRegistry.SuperAdminRoleId &&
                 request.ShopId.HasValue)
        {
            return Result<UserDetailResponse>.Failure(ErrorCodes.Forbidden,
                "အခြားဆိုင်အတွက် User ဖန်တီးခွင့် မရှိပါ။");
        }

        var user = User.Create(
            username,
            _hasher.Hash(request.Password),
            request.FullName,
            request.RoleId,
            actorId,
            _clock,
            shopId: shopId);
        if (phone is not null)
            user.UpdateProfile(fullName: null, phone: phone, actorId, _clock);

        _db.Users.Add(user);

        // Persist first so the generated Id lands in the audit row; both saves share
        // the single TxBehavior transaction, so this stays atomic.
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("USER.CREATE", "User", user.Id.ToString(),
            newValue: Serialize(user), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<UserDetailResponse>.Success(
            new UserDetailResponse(user.Id, user.Username, user.FullName, user.Phone,
                user.RoleId, roleCode, user.IsActive, user.LastLoginAtUtc,
                user.CreatedAtUtc, user.ModifiedAtUtc));
    }

    internal static string Serialize(User u) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            u.Id,
            u.Username,
            u.FullName,
            u.Phone,
            u.RoleId,
            u.IsActive
        });
}
