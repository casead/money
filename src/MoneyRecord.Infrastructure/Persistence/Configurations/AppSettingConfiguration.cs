using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Configurations;

/// <summary>DBD-005 T23 — AppSettings key-value configuration store.</summary>
public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("AppSettings");
        b.HasKey(x => x.Id);
        // Identity BY DEFAULT — seeded global rows keep explicit ids 1-10;
        // shop-scoped override rows get DB-generated ids.
        b.Property(x => x.Id).UseIdentityByDefaultColumn();
        b.Property(x => x.ShopId);
        b.Property(x => x.Key).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.HasIndex(x => new { x.Key, x.ShopId }).IsUnique()
            .HasDatabaseName("UQ_AppSettings_Key_Shop");
        b.Property(x => x.Value).HasMaxLength(500).IsRequired();
        b.Property(x => x.ValueType).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(x => x.IsSensitive).IsRequired().HasDefaultValue(false);
        b.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp(3) with time zone").IsRequired();

        // Default settings per API-007 SET-001 (values seeded; updatable at runtime).
        var clock = new SeedClock();
        b.HasData(
            new AppSetting(1, "shopName", "ကျွန်ုပ်၏ ဆိုင်", "string", false, clock),
            new AppSetting(2, "dayBoundaryOffsetHours", "0", "int", true, clock),
            new AppSetting(3, "pendingExpiryMinutes", "30", "int", false, clock),
            new AppSetting(4, "duplicateWindowMinutes", "5", "int", false, clock),
            new AppSetting(5, "txnAmountCap", "10000000", "int", false, clock),
            new AppSetting(6, "lowBalanceCashThreshold", "100000", "int", false, clock),
            new AppSetting(7, "lowBalanceFloatThresholdPerAccount", "50000", "int", false, clock),
            new AppSetting(8, "receiptFooterText", "ကျေးဇူးတင်ပါသည်", "string", false, clock),
            new AppSetting(9, "feePercentCashIn", "0", "percent", false, clock),
            new AppSetting(10, "feePercentCashOut", "0", "percent", false, clock));
    }
}
