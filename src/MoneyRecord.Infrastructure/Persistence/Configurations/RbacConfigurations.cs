using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MoneyRecord.UnitTests")]

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T03 — Permissions catalog. Seeded from Permissions.All (fixed ids).</summary>
public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // stable seed ids
        b.Property(x => x.Code).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_Permissions_Code");
        b.Property(x => x.Module).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);

        b.HasData(BuildSeeds());
    }

    /// <summary>Single source: seed rows derive from Domain registry so they can never drift.</summary>
    internal static Permission[] BuildSeeds() =>
        Permissions.All.Select((code, i) => new Permission(
            i + 1,
            code,
            ModuleOf(code),
            $"{code} permission")).ToArray();

    private static string ModuleOf(string code) => code switch
    {
        var c when c.StartsWith("txn.") => "Txn",
        var c when c.StartsWith("customer") => "Customer",
        var c when c.StartsWith("balance.") => "Balance",
        var c when c.StartsWith("fee.") => "Fee",
        var c when c.StartsWith("report.") => "Report",
        var c when c.StartsWith("tenant.") => "Platform",
        _ => "Admin"
    };
}

/// <summary>DBD-005 T04 — RolePermissions junction. Mirrors RolePermissionRegistry (test-asserted).</summary>
public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });

        b.HasIndex(x => x.PermissionId).HasDatabaseName("IX_RolePermissions_PermissionId");

        b.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasData(BuildSeeds());
    }

    internal static RolePermission[] BuildSeeds()
    {
        var idsByCode = PermissionConfiguration.BuildSeeds().ToDictionary(p => p.Code, p => p.Id);

        var mappings = new List<(int Role, string Code)>();

        // SuperAdmin: platform-level catalog only (M11 — no shop data access)
        foreach (var code in RolePermissionRegistry.PlatformPermissions)
            mappings.Add((RolePermissionRegistry.SuperAdminRoleId, code));

        // ShopAdmin: full shop-side catalog
        foreach (var code in new HashSet<string>(RolePermissionRegistry.ForRole(
                     RolePermissionRegistry.AdminRoleId)))
            mappings.Add((RolePermissionRegistry.AdminRoleId, code));

        // Staff: operational subset (SRS §5 matrix)
        mappings.Add((RolePermissionRegistry.StaffRoleId, Permissions.TxnCreate));
        mappings.Add((RolePermissionRegistry.StaffRoleId, Permissions.CustomerManage));
        mappings.Add((RolePermissionRegistry.StaffRoleId, Permissions.ReportDaily));

        return mappings
            .Select(m => new RolePermission(m.Role, idsByCode[m.Code]))
            .ToArray();
    }
}
