namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common.Errors;

/// <summary>
/// User-management guard rules (FR-006/007, UC-003/004).
/// Pure decision functions returning a stable errorCode when blocked, null when allowed —
/// keeps handlers thin and the rules unit-testable without infrastructure.
/// </summary>
public static class UserManagementRules
{
    /// <summary>
    /// Role change on self is always blocked (403 SELF_ROLE_CHANGE) and demoting the last
    /// active Admin would leave the shop without an admin (FR-007: Admin ≥ 1 အမြဲရှိရမည်).
    /// </summary>
    public static string? CheckRoleChange(long actorUserId, long targetUserId,
        bool demotesLastActiveAdmin)
    {
        if (actorUserId == targetUserId)
            return ErrorCodes.SelfRoleChange;

        if (demotesLastActiveAdmin)
            return ErrorCodes.LastAdmin;

        return null;
    }

    /// <summary>
    /// Status change on self is blocked (403 SELF_DEACTIVATE) and deactivating the last
    /// active Admin is blocked (403 LAST_ADMIN). Reactivation paths pass both checks.
    /// </summary>
    public static string? CheckStatusChange(long actorUserId, long targetUserId,
        bool deactivating, bool targetIsLastActiveAdmin)
    {
        if (actorUserId == targetUserId)
            return ErrorCodes.SelfDeactivate;

        if (deactivating && targetIsLastActiveAdmin)
            return ErrorCodes.LastAdmin;

        return null;
    }

    /// <summary>
    /// Only the platform owner (SuperAdmin) may grant the ShopAdmin role.
    /// ShopAdmins create/manage their own shop's Staff accounts only (USR-002/004).
    /// </summary>
    public static string? CheckRoleAssignment(int? actorRoleId, int requestedRoleId)
    {
        if (requestedRoleId == Common.Rbac.RolePermissionRegistry.AdminRoleId &&
            actorRoleId != Common.Rbac.RolePermissionRegistry.SuperAdminRoleId)
            return ErrorCodes.Forbidden;

        return null;
    }

    /// <summary>True when changing this user's role/status would remove the final active Admin.</summary>
    public static bool IsLastActiveAdmin(int activeAdminCountExcludingTarget, int targetRoleId) =>
        targetRoleId == Common.Rbac.RolePermissionRegistry.AdminRoleId &&
        activeAdminCountExcludingTarget == 0;

    /// <summary>
    /// M11 tenant guard: a shop-scoped actor (ShopId set) may only manage users of their
    /// own shop; SuperAdmin (ShopId null) is unrestricted. Target platform accounts
    /// (ShopId null) are off-limits to shop actors — they are not their staff.
    /// </summary>
    public static bool IsCrossTenant(long? actorShopId, long? targetShopId) =>
        actorShopId is { } own && targetShopId != own;
}
