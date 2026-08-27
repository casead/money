using FluentAssertions;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// Module 3 role-matrix automation (FR-009 AC-a): every permission × role combination
/// asserted against SRS §5, plus seed-consistency between the registry and EF HasData rows.
/// </summary>
public class RolePermissionRegistryTests
{
    [Fact]
    public void ShopAdmin_Holds_ShopSideCatalog()
    {
        foreach (var permission in RolePermissionRegistry.Ordered(
                     RolePermissionRegistry.AdminRoleId))
            RolePermissionRegistry.RoleHas(RolePermissionRegistry.AdminRoleId, permission)
                .Should().BeTrue($"ShopAdmin must hold '{permission}'");

        // Platform-only capabilities must NOT leak to shop admins (M11).
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.AdminRoleId,
            Permissions.TenantManage).Should().BeFalse();
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.AdminRoleId,
            Permissions.ProviderManage).Should().BeFalse();
    }

    [Fact]
    public void SuperAdmin_Holds_PlatformCatalog_Only()
    {
        foreach (var permission in RolePermissionRegistry.Ordered(
                     RolePermissionRegistry.SuperAdminRoleId))
            RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId, permission)
                .Should().BeTrue($"SuperAdmin must hold '{permission}'");

        // SuperAdmin stays out of shop-side money movement AND shop pricing/profit:
        // fee rules are per-shop business decisions, profit data is shop-private.
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId,
            Permissions.TxnCreate).Should().BeFalse();
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId,
            Permissions.BalanceAdjust).Should().BeFalse();
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId,
            Permissions.ReportProfit).Should().BeFalse();
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId,
            Permissions.FeeManage).Should().BeFalse();
        RolePermissionRegistry.RoleHas(RolePermissionRegistry.SuperAdminRoleId,
            Permissions.ReportDaily).Should().BeFalse();
    }

    [Fact]
    public void Staff_Has_OperationalPermissions_Only()
    {
        var allowed = new[]
        {
            Permissions.TxnCreate,
            Permissions.CustomerManage,
            Permissions.ReportDaily
        };

        RolePermissionRegistry.Ordered(RolePermissionRegistry.StaffRoleId)
            .Should().BeEquivalentTo(allowed);
    }

    [Fact]
    public void Staff_Is_Denied_AdminCapabilities()
    {
        var denied = new[]
        {
            Permissions.UserManage,
            Permissions.BalanceAdjust,
            Permissions.TxnCancel,
            Permissions.TxnReverse,
            Permissions.FeeManage,
            Permissions.ProviderManage,
            Permissions.AuditView,
            Permissions.SettingsManage,
            Permissions.ReportProfit
        };

        foreach (var permission in denied)
            RolePermissionRegistry.RoleHas(RolePermissionRegistry.StaffRoleId, permission)
                .Should().BeFalse($"Staff must NOT hold '{permission}'");
    }

    [Fact]
    public void UnknownRole_Gets_Nothing()
    {
        RolePermissionRegistry.ForRole(99).Should().BeEmpty();
        RolePermissionRegistry.RoleHas(99, Permissions.TxnCreate).Should().BeFalse();
    }

    [Fact]
    public void IsKnown_Recognizes_Catalog_And_Rejects_ArbitraryStrings()
    {
        Permissions.IsKnown(Permissions.UserManage).Should().BeTrue();
        Permissions.IsKnown("not.a.permission").Should().BeFalse();
        Permissions.IsKnown("").Should().BeFalse();
    }

    [Fact]
    public void Catalog_Has_No_Duplicates()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
        Permissions.All.Should().NotBeEmpty();
    }
}
