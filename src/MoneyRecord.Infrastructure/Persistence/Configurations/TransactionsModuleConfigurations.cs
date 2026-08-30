using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T09/T10 lookups.</summary>
public class TransactionLookupConfigurations
{
}

public class TransactionTypeConfiguration : IEntityTypeConfiguration<TransactionTypeSeed>
{
    public void Configure(EntityTypeBuilder<TransactionTypeSeed> b)
    {
        b.ToTable("TransactionTypes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(30).IsRequired();

        b.HasData(
            new TransactionTypeSeed(1, "CashIn", "Cash In"),
            new TransactionTypeSeed(2, "CashOut", "Cash Out"));
    }
}

public class TransactionStatusConfiguration : IEntityTypeConfiguration<TransactionStatusSeed>
{
    public void Configure(EntityTypeBuilder<TransactionStatusSeed> b)
    {
        b.ToTable("TransactionStatuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(15).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(30).IsRequired();

        b.HasData(
            new TransactionStatusSeed(1, "Pending", "Pending"),
            new TransactionStatusSeed(2, "Completed", "Completed"),
            new TransactionStatusSeed(3, "Cancelled", "Cancelled"),
            new TransactionStatusSeed(4, "Reversed", "Reversed"));
    }
}

/// <summary>DBD-005 T11 — Transactions (immutable financial core).</summary>
public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("Transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(x => x.TxnNo).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.TxnNo).IsUnique().HasDatabaseName("UQ_Transactions_TxnNo");

        b.Property(x => x.Type).IsRequired();
        b.Property(x => x.Status).IsRequired(); // domain always sets Completed at creation
        b.Property(x => x.Amount).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_Txn_Amount_Positive", "\"Amount\" > 0"));
        b.Property(x => x.FeeAmount).HasDefaultValue(0);
        b.ToTable(t => t.HasCheckConstraint("CK_Txn_FeeNonNeg", "\"FeeAmount\" >= 0"));
        b.Property(x => x.FeePaidVia).IsRequired().HasDefaultValue(FeePaidVia.Cash);
        b.Property(x => x.FeeDeductedFromAmount).HasDefaultValue(false);
        b.Property(x => x.NetAmount);
        b.Property(x => x.CommissionAmount).HasDefaultValue(0);
        b.ToTable(t => t.HasCheckConstraint("CK_Txn_CommNonNeg", "\"CommissionAmount\" >= 0"));

        b.Property(x => x.CustomerNameSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.CustomerPhoneSnapshot).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.CustomerPhoneSnapshot)
            .HasDatabaseName("IX_Transactions_CustomerPhoneSnapshot");

        b.Property(x => x.WalletProviderId).IsRequired();
        b.Property(x => x.WalletAccountId).IsRequired();

        // Tenancy snapshot (M11) — denormalized from WalletAccount for row-level scoping.
        b.Property(x => x.ShopId).IsRequired();
        b.HasIndex(x => new { x.ShopId, x.BusinessDate })
            .HasDatabaseName("IX_Transactions_ShopId_BusinessDate");

        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.ReferenceNo).HasMaxLength(50);
        b.HasIndex(x => x.ReferenceNo).HasDatabaseName("IX_Transactions_ReferenceNo");

        b.Property(x => x.IdempotencyKey).IsRequired();
        b.HasIndex(x => x.IdempotencyKey).IsUnique()
            .HasDatabaseName("UQ_Transactions_IdempotencyKey");

        // Report/search indexes (DBD IX-03 etc.)
        b.HasIndex(x => new { x.BusinessDate, x.Type, x.Status })
            .HasDatabaseName("IX_Transactions_BusinessDate_Type_Status");
        b.HasIndex(x => new { x.CustomerId, x.BusinessDate })
            .HasDatabaseName("IX_Transactions_CustomerId_BusinessDate");
        b.HasIndex(x => new { x.CreatedByUserId, x.BusinessDate })
            .HasDatabaseName("IX_Transactions_CreatedBy_BusinessDate");

        b.Property(x => x.BusinessDate).HasColumnType("date");
        b.Property(x => x.OccurredAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.CancelledAtUtc).HasColumnType("timestamp(3) with time zone");
        b.Property(x => x.ReversedAtUtc).HasColumnType("timestamp(3) with time zone");

        b.HasOne(x => x.WalletProvider)
            .WithMany()
            .HasForeignKey(x => x.WalletProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WalletAccount)
            .WithMany()
            .HasForeignKey(x => x.WalletAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Idempotency replay store table (BLUE-010 M6).</summary>
public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("IdempotencyKeys");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.Key).IsRequired();
        b.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UQ_IdempotencyKeys_Key");
        b.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.ResponseJson).IsRequired(false);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_IdempotencyKeys_ExpiresAt");
    }
}

/// <summary>Cancellation records (DBD-005 T12) — one per txn (UQ).</summary>
public class TransactionCancellationConfiguration : IEntityTypeConfiguration<TransactionCancellation>
{
    public void Configure(EntityTypeBuilder<TransactionCancellation> b)
    {
        b.ToTable("TransactionCancellations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.TransactionId).IsRequired();
        b.HasIndex(x => x.TransactionId).IsUnique().HasDatabaseName("UQ_TransactionCancellations_Txn");
        b.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        b.Property(x => x.CancelledAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.HasOne<Transaction>()
            .WithOne()
            .HasForeignKey<TransactionCancellation>(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Reversal links (DBD-005 T13) — MirrorTxnId UNIQUE (BR-027 terminal protection).</summary>
public class TransactionReversalConfiguration : IEntityTypeConfiguration<TransactionReversal>
{
    public void Configure(EntityTypeBuilder<TransactionReversal> b)
    {
        b.ToTable("TransactionReversals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        b.Property(x => x.OriginalTxnId).IsRequired();
        b.Property(x => x.MirrorTxnId).IsRequired();
        b.HasIndex(x => x.OriginalTxnId).IsUnique().HasDatabaseName("UQ_TransactionReversals_Original");
        b.HasIndex(x => x.MirrorTxnId).IsUnique().HasDatabaseName("UQ_TransactionReversals_Mirror");
        b.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        b.Property(x => x.ReversedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_TransactionReversals_NotSelf",
            "\"OriginalTxnId\" <> \"MirrorTxnId\""));
        b.HasOne<Transaction>()
            .WithOne()
            .HasForeignKey<TransactionReversal>(x => x.OriginalTxnId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Transaction>()
            .WithOne()
            .HasForeignKey<TransactionReversal>(x => x.MirrorTxnId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
