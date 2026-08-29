using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T05 — Customers (soft-delete registry).</summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.FullName).HasDatabaseName("IX_Customers_FullName");

        // Canonical Myanmar format — ASCII VARCHAR(20)
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false).IsRequired();

        // UNIQUE per shop among non-deleted rows — the same phone may exist in
        // different shops (per-shop isolation), never twice within one shop.
        b.HasIndex(x => new { x.ShopId, x.Phone })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_Customers_Shop_Phone");

        b.Property(x => x.Address).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.IsBookmarked).IsRequired().HasDefaultValue(false);

        // Per-shop tenancy (M11 isolation) — every customer belongs to one shop.
        b.Property(x => x.ShopId);
        b.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ShopId).HasDatabaseName("IX_Customers_ShopId");

        b.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ModifiedAtUtc).HasColumnType("timestamp(3) with time zone");

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ModifiedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global query filter: soft-deleted customers invisible in default lists
        b.HasQueryFilter(x => !x.IsDeleted);

        // Table-level CHECK: name >= 2 chars (DBD constraint)
        b.ToTable(t => t.HasCheckConstraint("CK_Customers_FullName_Length", "LENGTH(\"FullName\") >= 2"));
    }
}
