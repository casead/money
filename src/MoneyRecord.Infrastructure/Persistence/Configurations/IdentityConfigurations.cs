using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T01 — Users.</summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.Username)
            .HasMaxLength(50)
            .IsRequired();
        b.HasIndex(x => x.Username).IsUnique().HasDatabaseName("UQ_Users_Username");

        // Hash format is ASCII — VARCHAR(255) per DBD
        b.Property(x => x.PasswordHash)
            .HasMaxLength(255)
            .IsRequired()
            .IsUnicode(false);

        b.Property(x => x.FullName).HasMaxLength(100).IsRequired();

        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        // UNIQUE filtered: only non-null, non-deleted rows (DBD IX)
        b.HasIndex(x => x.Phone)
            .IsUnique()
            .HasFilter("\"Phone\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_Users_Phone");

        b.Property(x => x.RoleId).IsRequired();
        b.HasIndex(x => x.RoleId).HasDatabaseName("IX_Users_RoleId");

        // Tenancy (M11): null = platform account (SuperAdmin)
        b.Property(x => x.ShopId);
        b.HasIndex(x => x.ShopId).HasDatabaseName("IX_Users_ShopId");
        b.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        b.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);
        b.Property(x => x.FailedLoginCount).IsRequired();

        // TOTP MFA (SEC hardening): Base32 secrets are ASCII
        b.Property(x => x.MfaEnabled).IsRequired().HasDefaultValue(false);
        b.Property(x => x.MfaSecret).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.MfaPendingSecret).HasMaxLength(64).IsUnicode(false);

        b.Property(x => x.LastLoginAtUtc).HasColumnType("timestamp(0) with time zone");
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ModifiedAtUtc).HasColumnType("timestamp(3) with time zone");

        b.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global query filter: soft-deleted users invisible everywhere
        b.HasQueryFilter(x => !x.IsDeleted);

        // Table-level CHECK: username >= 3 chars (DBD constraint)
        b.ToTable(t => t.HasCheckConstraint("CK_Users_Username_Length", "LENGTH(\"Username\") >= 3"));
    }
}

/// <summary>Shops (tenants) — M11 multi-tenancy root.</summary>
public class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> b)
    {
        b.ToTable("Shops");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_Shops_Code");

        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).IsRequired().HasDefaultValue(Shop.ActiveStatus);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ModifiedAtUtc).HasColumnType("timestamp(3) with time zone");
    }
}

/// <summary>DBD-005 T02 — Roles. Seeded SuperAdmin/ShopAdmin(Admin)/Staff.</summary>
public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // fixed seed ids 1..3
        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_Roles_Code");
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.IsSystemRole).IsRequired().HasDefaultValue(false);

        b.HasData(
            new Role(1, "SuperAdmin", "Super Admin", "Platform owner", isSystemRole: true),
            new Role(2, "Admin", "Admin", "Shop administrator", isSystemRole: true),
            new Role(3, "Staff", "Staff", "Shop staff", isSystemRole: true));
    }
}

/// <summary>Refresh tokens (BLUE-010 M2) — hashed storage, rotation chain.</summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.TokenHash)
            .HasMaxLength(64) // SHA-256 hex = 64 chars
            .IsUnicode(false)
            .IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UQ_RefreshTokens_TokenHash");

        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(64).IsUnicode(false);

        b.Property(x => x.DeviceInfo).HasMaxLength(200);
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);

        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.RevokedAtUtc).HasColumnType("timestamp(3) with time zone");

        b.HasIndex(x => new { x.UserId, x.RevokedAtUtc }).HasDatabaseName("IX_RefreshTokens_User_Revoked");

        // Matching query filter so soft-deleted users' tokens don't warn/orphan (EF required-end filter rule)
        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(t => t.User.IsDeleted == false);
    }
}

/// <summary>DBD-005 T22 — AuditLogs. INSERT-only by convention (no update/delete APIs exposed).</summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.ActionCode).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(30).IsUnicode(false).IsRequired();

        b.Property(x => x.OldValuesJson).IsRequired(false);
        b.Property(x => x.NewValuesJson).IsRequired(false);

        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.Property(x => x.DeviceInfo).HasMaxLength(200);

        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ActorUserId);

        // Tenancy (M11): null = platform-level action
        b.Property(x => x.ShopId);
        b.HasIndex(x => x.ShopId).HasDatabaseName("IX_AuditLogs_ShopId_CreatedAt");

        b.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAtUtc })
            .HasDatabaseName("IX_AuditLogs_EntityType_EntityId_CreatedAt");
        b.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc })
            .HasDatabaseName("IX_AuditLogs_Actor_CreatedAt");
        b.HasIndex(x => new { x.ActionCode, x.CreatedAtUtc })
            .HasDatabaseName("IX_AuditLogs_ActionCode_CreatedAt");
    }
}
