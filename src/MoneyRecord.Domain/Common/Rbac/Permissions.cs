namespace MoneyRecord.Domain.Common.Rbac;

/// <summary>
/// Permission code constants (API-007 §13.2, DBD-005 T03).
/// Codes are stable API contract values — never rename without a version bump.
/// </summary>
public static class Permissions
{
    // Txn
    public const string TxnCreate = "txn.create";
    public const string TxnCancel = "txn.cancel";
    public const string TxnReverse = "txn.reverse";

    // Customer
    public const string CustomerManage = "customer.manage";

    // Balance
    public const string BalanceAdjust = "balance.adjust";

    // Fee
    public const string FeeManage = "fee.manage";

    // Admin / config
    public const string TenantManage = "tenant.manage";
    public const string ProviderManage = "provider.manage";
    public const string UserManage = "user.manage";
    public const string AuditView = "audit.view";
    public const string SettingsManage = "settings.manage";

    // Report
    public const string ReportDaily = "report.daily";
    public const string ReportProfit = "report.profit";

    /// <summary>All known permission codes (catalog order = seed id order).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        TxnCreate, TxnCancel, TxnReverse,
        CustomerManage,
        BalanceAdjust,
        FeeManage,
        TenantManage, ProviderManage, UserManage, AuditView, SettingsManage,
        ReportDaily, ReportProfit
    ];

    /// <summary>True when <paramref name="code"/> is a recognized permission policy name.</summary>
    public static bool IsKnown(string code) => All.Contains(code);
}
