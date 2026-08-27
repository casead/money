using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T19 — FeeRules (effective-dated, immutable once in force).</summary>
public class FeeRuleConfiguration : IEntityTypeConfiguration<FeeRule>
{
    public void Configure(EntityTypeBuilder<FeeRule> b)
    {
        b.ToTable("FeeRules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.WalletProviderId).IsRequired();
        b.Property(x => x.CalculationType).IsRequired();
        b.Property(x => x.FlatAmount);
        b.ToTable(t => t.HasCheckConstraint("CK_FeeRule_Flat_Positive",
            "\"CalculationType\" <> 1 OR \"FlatAmount\" > 0"));
        b.Property(x => x.PercentValue).HasColumnType("numeric(5,4)");
        b.ToTable(t => t.HasCheckConstraint("CK_FeeRule_Percent_Range",
            "\"CalculationType\" <> 2 OR (\"PercentValue\" > 0 AND \"PercentValue\" <= 100)"));
        b.ToTable(t => t.HasCheckConstraint("CK_FeeRule_MinMax",
            "\"MinFee\" IS NULL OR \"MaxFee\" IS NULL OR \"MaxFee\" >= \"MinFee\""));

        b.Property(x => x.EffectiveFromUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.EffectiveToUtc).HasColumnType("timestamp(3) with time zone");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ModifiedAtUtc).HasColumnType("timestamp(3) with time zone");

        // DBD: IX_FeeRules_Provider_EffectiveFrom DESC
        b.HasIndex(x => new { x.WalletProviderId, x.EffectiveFromUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_FeeRules_Provider_EffectiveFrom");

        b.HasOne(x => x.WalletProvider)
            .WithMany()
            .HasForeignKey(x => x.WalletProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>DBD-005 T20B — CommissionSources lookup with seed.</summary>
public class CommissionSourceConfiguration : IEntityTypeConfiguration<CommissionSource>
{
    public void Configure(EntityTypeBuilder<CommissionSource> b)
    {
        b.ToTable("CommissionSources");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_CommissionSources_Code");
        b.Property(x => x.Name).HasMaxLength(30).IsRequired();

        b.HasData(
            new { Id = (byte)1, Code = "PerTxnAuto", Name = "Per-Txn Auto" },
            new { Id = (byte)2, Code = "PerTxnManual", Name = "Per-Txn Manual" },
            new { Id = (byte)3, Code = "PeriodicBatch", Name = "Periodic Batch" },
            new { Id = (byte)4, Code = "Adjustment", Name = "Adjustment" });
    }
}

/// <summary>DBD-005 T21 — CommissionEntries (append-only, XOR source ref).</summary>
public class CommissionEntryConfiguration : IEntityTypeConfiguration<CommissionEntry>
{
    public void Configure(EntityTypeBuilder<CommissionEntry> b)
    {
        b.ToTable("CommissionEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_CommissionEntry_Amount_Positive", "\"Amount\" > 0"));
        b.ToTable(t => t.HasCheckConstraint("CK_CommissionEntry_Source_Xor",
            "(\"TransactionId\" IS NOT NULL AND \"BatchRef\" IS NULL) OR (\"BatchRef\" IS NOT NULL AND \"TransactionId\" IS NULL)"));
        b.Property(x => x.BatchRef).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        b.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_CommissionEntries_CreatedAt");

        b.HasOne(x => x.Transaction)
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Source)
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
