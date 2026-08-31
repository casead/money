using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;
using MongoDB.EntityFrameworkCore.Extensions;
using MongoDB.Driver;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MoneyRecord.UnitTests")]

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Application DbContext backed by MongoDB via MongoDB.EntityFrameworkCore provider.
/// Acts as the Unit of Work via SaveChangesAsync (ARCH-006 §11).
/// </summary>
public class MoneyRecordDbContext : DbContext
{
    private readonly IClock? _clock;

    /// <summary>Direct IMongoDatabase instance for value generators. Set during DI registration.</summary>
    internal static IMongoDatabase? MongoDatabaseInstance { get; set; }

    public MoneyRecordDbContext(DbContextOptions<MoneyRecordDbContext> options,
        IClock? clock = null)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<WalletProvider> WalletProviders => Set<WalletProvider>();
    public DbSet<WalletAccount> WalletAccounts => Set<WalletAccount>();
    public DbSet<PhysicalCashAccount> PhysicalCashAccounts => Set<PhysicalCashAccount>();
    public DbSet<CashLedgerEntry> CashLedgerEntries => Set<CashLedgerEntry>();
    public DbSet<WalletLedgerEntry> WalletLedgerEntries => Set<WalletLedgerEntry>();
    public DbSet<AdjustmentType> AdjustmentTypes => Set<AdjustmentType>();
    public DbSet<CashAdjustment> CashAdjustments => Set<CashAdjustment>();
    public DbSet<FloatAdjustment> FloatAdjustments => Set<FloatAdjustment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionCancellation> TransactionCancellations => Set<TransactionCancellation>();
    public DbSet<TransactionReversal> TransactionReversals => Set<TransactionReversal>();
    public DbSet<FeeRule> FeeRules => Set<FeeRule>();
    public DbSet<CommissionSource> CommissionSources => Set<CommissionSource>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<TransactionTypeSeed> TransactionTypeSeeds => Set<TransactionTypeSeed>();
    public DbSet<TransactionStatusSeed> TransactionStatusSeeds => Set<TransactionStatusSeed>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MongoDB collection names
        modelBuilder.Entity<User>().ToCollection("users");
        modelBuilder.Entity<Role>().ToCollection("roles");
        modelBuilder.Entity<Shop>().ToCollection("shops");
        modelBuilder.Entity<Permission>().ToCollection("permissions");
        modelBuilder.Entity<RolePermission>().ToCollection("rolePermissions");
        modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
        modelBuilder.Entity<RefreshToken>().ToCollection("refreshTokens");
        modelBuilder.Entity<AuditLog>().ToCollection("auditLogs");
        modelBuilder.Entity<Customer>().ToCollection("customers");
        modelBuilder.Entity<WalletProvider>().ToCollection("walletProviders");
        modelBuilder.Entity<WalletAccount>().ToCollection("walletAccounts");
        modelBuilder.Entity<PhysicalCashAccount>().ToCollection("physicalCashAccounts");
        modelBuilder.Entity<CashLedgerEntry>().ToCollection("cashLedgerEntries");
        modelBuilder.Entity<WalletLedgerEntry>().ToCollection("walletLedgerEntries");
        modelBuilder.Entity<AdjustmentType>().ToCollection("adjustmentTypes");
        modelBuilder.Entity<CashAdjustment>().ToCollection("cashAdjustments");
        modelBuilder.Entity<FloatAdjustment>().ToCollection("floatAdjustments");
        modelBuilder.Entity<Transaction>().ToCollection("transactions");
        modelBuilder.Entity<TransactionCancellation>().ToCollection("transactionCancellations");
        modelBuilder.Entity<TransactionReversal>().ToCollection("transactionReversals");
        modelBuilder.Entity<FeeRule>().ToCollection("feeRules");
        modelBuilder.Entity<CommissionSource>().ToCollection("commissionSources");
        modelBuilder.Entity<CommissionEntry>().ToCollection("commissionEntries");
        modelBuilder.Entity<IdempotencyKey>().ToCollection("idempotencyKeys");
        modelBuilder.Entity<AppSetting>().ToCollection("appSettings");
        modelBuilder.Entity<TransactionTypeSeed>().ToCollection("transactionTypes");
        modelBuilder.Entity<TransactionStatusSeed>().ToCollection("transactionStatuses");

        // DateOnly ↔ DateTime conversion for MongoDB (no native DateOnly support)
        modelBuilder.Entity<Transaction>()
            .Property(t => t.BusinessDate)
            .HasConversion(
                v => v.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                v => DateOnly.FromDateTime(v));

        // Seed data
        SeedRoles(modelBuilder);
        SeedPermissions(modelBuilder);
        SeedRolePermissions(modelBuilder);
        SeedWalletProviders(modelBuilder);
        SeedAdjustmentTypes(modelBuilder);
        SeedTransactionTypes(modelBuilder);
        SeedTransactionStatuses(modelBuilder);
        SeedAppSettings(modelBuilder);

        // MongoDB value generators for long primary keys
        if (MongoDatabaseInstance is not null)
        {
            var generator = new MongoValueGenerator(MongoDatabaseInstance);
            var intGenerator = new MongoIntValueGenerator(MongoDatabaseInstance);
            modelBuilder.Entity<User>().Property(u => u.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<Shop>().Property(s => s.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<Customer>().Property(c => c.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<WalletProvider>().Property(wp => wp.Id).HasValueGenerator((_, _) => intGenerator);
            modelBuilder.Entity<WalletAccount>().Property(w => w.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<CashLedgerEntry>().Property(e => e.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<WalletLedgerEntry>().Property(e => e.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<CashAdjustment>().Property(a => a.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<FloatAdjustment>().Property(a => a.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<Transaction>().Property(t => t.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<TransactionCancellation>().Property(c => c.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<TransactionReversal>().Property(r => r.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<CommissionEntry>().Property(e => e.Id).HasValueGenerator((_, _) => generator);
            modelBuilder.Entity<IdempotencyKey>().Property(k => k.Id).HasValueGenerator((_, _) => generator);
        }

        base.OnModelCreating(modelBuilder);
    }

    private static void SeedRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role(1, "SuperAdmin", "Super Admin", "Platform owner", isSystemRole: true),
            new Role(2, "Admin", "Admin", "Shop administrator", isSystemRole: true),
            new Role(3, "Staff", "Staff", "Shop staff", isSystemRole: true));
    }

    private static void SeedPermissions(ModelBuilder modelBuilder)
    {
        var permissions = Domain.Common.Rbac.Permissions.All
            .Select((code, i) => new Permission(i + 1, code, ModuleOf(code), $"{code} permission"))
            .ToArray();
        modelBuilder.Entity<Permission>().HasData(permissions);
    }

    private static void SeedRolePermissions(ModelBuilder modelBuilder)
    {
        var idsByCode = Domain.Common.Rbac.Permissions.All
            .Select((code, i) => (Code: code, Id: i + 1))
            .ToDictionary(x => x.Code, x => x.Id);

        var mappings = new List<(int Role, string Code)>();

        foreach (var code in Domain.Common.Rbac.RolePermissionRegistry.PlatformPermissions)
            mappings.Add((1, code));

        foreach (var code in new HashSet<string>(Domain.Common.Rbac.RolePermissionRegistry.ForRole(2)))
            mappings.Add((2, code));

        mappings.Add((3, Domain.Common.Rbac.Permissions.TxnCreate));
        mappings.Add((3, Domain.Common.Rbac.Permissions.CustomerManage));
        mappings.Add((3, Domain.Common.Rbac.Permissions.ReportDaily));

        var rolePerms = mappings
            .Select(m => new RolePermission(m.Role, idsByCode[m.Code]))
            .ToArray();
        modelBuilder.Entity<RolePermission>().HasData(rolePerms);
    }

    private static void SeedWalletProviders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletProvider>().HasData(
            new WalletProvider("WAVE", "Wave Money", null, 1, id: 1),
            new WalletProvider("KBZPAY", "KBZPay", null, 2, id: 2));
    }

    private static void SeedAdjustmentTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdjustmentType>().HasData(
            new AdjustmentType(AdjustmentType.CashCorrectionId, "CashCorrection", "Cash Correction"),
            new AdjustmentType(AdjustmentType.FloatTopUpId, "FloatTopUp", "Float Top-up"),
            new AdjustmentType(AdjustmentType.FloatWithdrawalId, "FloatWithdrawal", "Float Withdrawal"));
    }

    private static void SeedTransactionTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionTypeSeed>().HasData(
            new TransactionTypeSeed(1, "CashIn", "Cash In"),
            new TransactionTypeSeed(2, "CashOut", "Cash Out"));
    }

    private static void SeedTransactionStatuses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionStatusSeed>().HasData(
            new TransactionStatusSeed(1, "Pending", "Pending"),
            new TransactionStatusSeed(2, "Completed", "Completed"),
            new TransactionStatusSeed(3, "Cancelled", "Cancelled"),
            new TransactionStatusSeed(4, "Reversed", "Reversed"));
    }

    private static void SeedAppSettings(ModelBuilder modelBuilder)
    {
        var clock = new SeedClock();
        modelBuilder.Entity<AppSetting>().HasData(
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

    /// <summary>Builds permission seed data (used by tests for consistency checks).</summary>
    internal static Permission[] BuildPermissionSeeds() =>
        Domain.Common.Rbac.Permissions.All
            .Select((code, i) => new Permission(i + 1, code, ModuleOf(code), $"{code} permission"))
            .ToArray();

    /// <summary>Builds role-permission seed data (used by tests for consistency checks).</summary>
    internal static RolePermission[] BuildRolePermissionSeeds()
    {
        var idsByCode = Domain.Common.Rbac.Permissions.All
            .Select((code, i) => (Code: code, Id: i + 1))
            .ToDictionary(x => x.Code, x => x.Id);

        var mappings = new List<(int Role, string Code)>();

        foreach (var code in Domain.Common.Rbac.RolePermissionRegistry.PlatformPermissions)
            mappings.Add((1, code));

        foreach (var code in new HashSet<string>(Domain.Common.Rbac.RolePermissionRegistry.ForRole(2)))
            mappings.Add((2, code));

        mappings.Add((3, Domain.Common.Rbac.Permissions.TxnCreate));
        mappings.Add((3, Domain.Common.Rbac.Permissions.CustomerManage));
        mappings.Add((3, Domain.Common.Rbac.Permissions.ReportDaily));

        return mappings
            .Select(m => new RolePermission(m.Role, idsByCode[m.Code]))
            .ToArray();
    }
}

internal sealed class SeedClock : IClock
{
    public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateOnly TodayYangon => DateOnly.FromDateTime(UtcNow);
}
