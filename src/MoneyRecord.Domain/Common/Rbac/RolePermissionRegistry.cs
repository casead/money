namespace MoneyRecord.Domain.Common.Rbac;

/// <summary>
/// Single source of truth: role → permission set (SRS §5 matrix + M11 multi-tenant).
/// Role ids mirror the seeded Roles rows (1=SuperAdmin, 2=ShopAdmin("Admin"), 3=Staff).
/// Must stay consistent with the RolePermissions seed data (asserted by unit tests).
/// </summary>
public static class RolePermissionRegistry
{
    public const int SuperAdminRoleId = 1;
    public const int AdminRoleId = 2;      // ShopAdmin — legacy name kept for call sites/tests
    public const int StaffRoleId = 3;

    // ---- Platform-level catalog (SuperAdmin only) ----
    // Platform owner = account control + provider/tenant management only.
    // Fee rules are SHOP business decisions (each shop prices differently) and
    // profit data is shop-private — neither belongs to the platform role.
    public static readonly IReadOnlyList<string> PlatformPermissions =
    [
        Permissions.TenantManage,
        Permissions.ProviderManage,
        Permissions.UserManage,
        Permissions.AuditView,
        Permissions.SettingsManage
    ];

    // ---- Shop-side catalog ----
    private static readonly IReadOnlySet<string> ShopAdminPermissions =
        new HashSet<string>
        {
            Permissions.TxnCreate, Permissions.TxnCancel, Permissions.TxnReverse,
            Permissions.CustomerManage,
            Permissions.BalanceAdjust,
            Permissions.FeeManage,        // per-shop fee/commission pricing (owner decision)
            Permissions.ReportDaily, Permissions.ReportProfit,
            Permissions.SettingsManage,   // shop-scoped settings only
            Permissions.UserManage,       // own-shop staff management only
            Permissions.AuditView         // own-shop audit only
        };

    // Staff: create txns, manage customers, daily reports (no profit), nothing else (SRS §5)
    private static readonly IReadOnlySet<string> StaffPermissions =
        new HashSet<string>
        {
            Permissions.TxnCreate,
            Permissions.CustomerManage,
            Permissions.ReportDaily
        };

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public static IReadOnlySet<string> ForRole(int roleId) => roleId switch
    {
        SuperAdminRoleId => new HashSet<string>(PlatformPermissions),
        AdminRoleId => ShopAdminPermissions,
        StaffRoleId => StaffPermissions,
        _ => Empty
    };

    /// <summary>Server-side permission check (SEC-003 — client checks are UX only).</summary>
    public static bool RoleHas(int roleId, string permission) =>
        ForRole(roleId).Contains(permission);

    /// <summary>Deterministic ordering for API responses.</summary>
    public static string[] Ordered(int roleId) =>
        [.. ForRole(roleId).Order()];
}
