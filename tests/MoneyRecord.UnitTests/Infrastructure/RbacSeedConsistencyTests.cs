using FluentAssertions;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Infrastructure.Persistence;

namespace MoneyRecord.UnitTests.Infrastructure;

/// <summary>
/// Guarantees the EF HasData seeds (DBD T03/T04) can never drift from the
/// code-level RBAC registry — the single enforcement source (Module 3).
/// </summary>
public class RbacSeedConsistencyTests
{
    [Fact]
    public void PermissionSeeds_Mirror_RegistryCatalog()
    {
        var seeds = MoneyRecordDbContext.BuildPermissionSeeds();

        seeds.Select(p => p.Code)
            .Should().BeEquivalentTo(Permissions.All);
        seeds.Select((p, i) => p.Id)
            .Should().BeInAscendingOrder("stable seed ids 1..N");
    }

    [Fact]
    public void RolePermissionSeeds_Mirror_RoleRegistry()
    {
        var seeds = MoneyRecordDbContext.BuildRolePermissionSeeds();
        var codeById = MoneyRecordDbContext.BuildPermissionSeeds()
            .ToDictionary(p => p.Id, p => p.Code);

        // ShopAdmin ("Admin", role 2): shop-side catalog only (M11)
        var adminCodes = seeds.Where(r => r.RoleId == RolePermissionRegistry.AdminRoleId)
            .Select(r => codeById[r.PermissionId]);
        adminCodes.Should().BeEquivalentTo(
            RolePermissionRegistry.Ordered(RolePermissionRegistry.AdminRoleId));

        var staffCodes = seeds.Where(r => r.RoleId == RolePermissionRegistry.StaffRoleId)
            .Select(r => codeById[r.PermissionId]);
        staffCodes.Should().BeEquivalentTo(RolePermissionRegistry.Ordered(RolePermissionRegistry.StaffRoleId));

        // SuperAdmin (role 1): platform-level catalog (M11)
        var superCodes = seeds.Where(r => r.RoleId == RolePermissionRegistry.SuperAdminRoleId)
            .Select(r => codeById[r.PermissionId]);
        superCodes.Should().BeEquivalentTo(
            RolePermissionRegistry.Ordered(RolePermissionRegistry.SuperAdminRoleId));
    }
}
