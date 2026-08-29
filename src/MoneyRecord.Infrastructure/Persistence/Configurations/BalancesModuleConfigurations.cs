using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T06 — WalletProviders (seeded WAVE/KBZPAY).</summary>
public class WalletProviderConfiguration : IEntityTypeConfiguration<WalletProvider>
{
    public void Configure(EntityTypeBuilder<WalletProvider> b)
    {
        b.ToTable("WalletProviders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_WalletProviders_Code");
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(300);
        b.Property(x => x.DisplayOrder).HasDefaultValue(0);
        b.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        b.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);

        b.HasData(
            new WalletProvider("WAVE", "Wave Money", null, 1, id: 1),
            new WalletProvider("KBZPAY", "KBZPay", null, 2, id: 2));
    }
}

/// <summary>DBD-005 T07 — WalletAccounts (cached float, non-negative CHECK).</summary>
public class WalletAccountConfiguration : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(EntityTypeBuilder<WalletAccount> b)
    {
        b.ToTable("WalletAccounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.AccountName).HasMaxLength(100).IsRequired();
        b.Property(x => x.AccountNumber).HasMaxLength(30).IsUnicode(false);
        b.HasIndex(x => new { x.WalletProviderId, x.AccountNumber })
            .IsUnique()
            .HasFilter("\"AccountNumber\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("UQ_WalletAccounts_Provider_AccountNumber");

        b.Property(x => x.CurrentFloatBalance).HasDefaultValue(0);
        b.ToTable(t => t.HasCheckConstraint("CK_WalletAccounts_Float_Nonneg",
            "\"CurrentFloatBalance\" >= 0"));

        // Tenancy (M11)
        b.Property(x => x.ShopId).IsRequired();
        b.HasIndex(x => x.ShopId).HasDatabaseName("IX_WalletAccounts_ShopId");

        b.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        b.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ModifiedAtUtc).HasColumnType("timestamp(3) with time zone");
        b.Property(x => x.WalletProviderId).IsRequired();
        b.HasIndex(x => x.WalletProviderId).HasDatabaseName("IX_WalletAccounts_ProviderId");

        b.HasOne(x => x.WalletProvider)
            .WithMany()
            .HasForeignKey(x => x.WalletProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

/// <summary>DBD-005 T08 — PhysicalCashAccount per shop (seed id=1 shop, balance 0).</summary>
public class PhysicalCashAccountConfiguration : IEntityTypeConfiguration<PhysicalCashAccount>
{
    public void Configure(EntityTypeBuilder<PhysicalCashAccount> b)
    {
        b.ToTable("PhysicalCashAccounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CurrentCashBalance).HasDefaultValue(0);
        b.ToTable(t => t.HasCheckConstraint("CK_PhysicalCash_Nonneg",
            "\"CurrentCashBalance\" >= 0"));
        b.Property(x => x.LastReconciledAtUtc).HasColumnType("timestamp(3) with time zone");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp(3) with time zone");

        // Default shop cash pool (M11: one row per shop; Id == ShopId).
        b.HasData(PhysicalCashAccount.CreateSingleton(0,
            new SeedClock()));
    }
}

/// <summary>DBD-005 T18 lookup seeds.</summary>
public class AdjustmentTypeConfiguration : IEntityTypeConfiguration<AdjustmentType>
{
    public void Configure(EntityTypeBuilder<AdjustmentType> b)
    {
        b.ToTable("AdjustmentTypes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_AdjustmentTypes_Code");
        b.Property(x => x.Name).HasMaxLength(30).IsRequired();

        b.HasData(
            new AdjustmentType(AdjustmentType.CashCorrectionId, "CashCorrection", "Cash Correction"),
            new AdjustmentType(AdjustmentType.FloatTopUpId, "FloatTopUp", "Float Top-up"),
            new AdjustmentType(AdjustmentType.FloatWithdrawalId, "FloatWithdrawal", "Float Withdrawal"));
    }
}

/// <summary>T14/T15 shared configuration helper — append-only ledgers.</summary>
public class LedgerConfigurations
{
}

public class CashLedgerEntryConfiguration : IEntityTypeConfiguration<CashLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CashLedgerEntry> b)
    {
        b.ToTable("CashLedgerEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.Direction).IsRequired();
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_CashLedger_Amount_Positive", "\"Amount\" > 0"));
        b.Property(x => x.SourceType).IsRequired();

        // XOR source refs (DBD constraint)
        b.ToTable(t => t.HasCheckConstraint("CK_CashLedger_Source_Xor",
            "(\"SourceType\" = 1 AND \"TransactionId\" IS NOT NULL AND \"CashAdjustmentId\" IS NULL) OR " +
            "(\"SourceType\" = 2 AND \"CashAdjustmentId\" IS NOT NULL AND \"TransactionId\" IS NULL)"));

        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        b.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_CashLedger_CreatedAt");
        b.HasIndex(x => x.TransactionId)
            .HasFilter("\"TransactionId\" IS NOT NULL")
            .HasDatabaseName("IX_CashLedger_TransactionId");
        b.HasIndex(x => x.CashAdjustmentId)
            .HasFilter("\"CashAdjustmentId\" IS NOT NULL")
            .HasDatabaseName("IX_CashLedger_CashAdjustmentId");

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class WalletLedgerEntryConfiguration : IEntityTypeConfiguration<WalletLedgerEntry>
{
    public void Configure(EntityTypeBuilder<WalletLedgerEntry> b)
    {
        b.ToTable("WalletLedgerEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.WalletAccountId).IsRequired();
        b.Property(x => x.Direction).IsRequired();
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_WalletLedger_Amount_Positive", "\"Amount\" > 0"));
        b.Property(x => x.SourceType).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_WalletLedger_Source_Xor",
            "(\"SourceType\" = 1 AND \"TransactionId\" IS NOT NULL AND \"FloatAdjustmentId\" IS NULL) OR " +
            "(\"SourceType\" = 2 AND \"FloatAdjustmentId\" IS NOT NULL AND \"TransactionId\" IS NULL)"));

        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        b.HasIndex(x => new { x.WalletAccountId, x.CreatedAtUtc })
            .HasDatabaseName("IX_WalletLedger_AccountId_CreatedAt");

        b.HasOne<WalletAccount>()
            .WithMany()
            .HasForeignKey(x => x.WalletAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CashAdjustmentConfiguration : IEntityTypeConfiguration<CashAdjustment>
{
    public void Configure(EntityTypeBuilder<CashAdjustment> b)
    {
        b.ToTable("CashAdjustments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.AdjustmentTypeId).IsRequired();
        b.Property(x => x.Direction).IsRequired();
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_CashAdj_Amount_Positive", "\"Amount\" > 0"));
        b.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        b.Property(x => x.BalanceAfter).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        b.HasOne<AdjustmentType>()
            .WithMany()
            .HasForeignKey(x => x.AdjustmentTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FloatAdjustmentConfiguration : IEntityTypeConfiguration<FloatAdjustment>
{
    public void Configure(EntityTypeBuilder<FloatAdjustment> b)
    {
        b.ToTable("FloatAdjustments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.WalletAccountId).IsRequired();
        b.Property(x => x.AdjustmentTypeId).IsRequired();
        b.Property(x => x.Direction).IsRequired();
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_FloatAdj_Amount_Positive", "\"Amount\" > 0"));
        b.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        b.Property(x => x.BalanceAfter).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        b.HasOne<WalletAccount>()
            .WithMany()
            .HasForeignKey(x => x.WalletAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdjustmentType>()
            .WithMany()
            .HasForeignKey(x => x.AdjustmentTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.WalletAccountId).HasDatabaseName("IX_FloatAdjustments_Account");
    }
}

/// <summary>Design-time clock for HasData seeds (fixed UTC instant; values are placeholders).</summary>
internal sealed class SeedClock : MoneyRecord.Application.Common.Interfaces.IClock
{
    public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateOnly TodayYangon => DateOnly.FromDateTime(UtcNow);
}
