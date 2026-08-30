using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds reference data that MongoDB ignores from HasData().
/// Roles, Permissions, RolePermissions, WalletProviders, etc.
/// Must run BEFORE AdminSeeder so the admin's role lookup works.
/// </summary>
public static class ReferenceDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MoneyRecordDbContext>();
        var clock = services.GetRequiredService<Application.Common.Interfaces.IClock>();
        var logger = services.GetRequiredService<ILogger<MoneyRecordDbContext>>();

        await db.Database.EnsureCreatedAsync();

        // 1. Roles
        if (!await db.Roles.AnyAsync())
        {
            db.Roles.AddRange(
                new Role(1, "SuperAdmin", "Super Admin", "Platform owner", isSystemRole: true),
                new Role(2, "Admin", "Admin", "Shop administrator", isSystemRole: true),
                new Role(3, "Staff", "Staff", "Shop staff", isSystemRole: true));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} roles.", 3);
        }

        // 2. Permissions
        if (!await db.Permissions.AnyAsync())
        {
            var permissions = Domain.Common.Rbac.Permissions.All
                .Select((code, i) => new Permission(i + 1, code,
                    ModuleOf(code), $"{code} permission"))
                .ToArray();
            db.Permissions.AddRange(permissions);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} permissions.", permissions.Length);
        }

        // 3. RolePermissions
        if (!await db.RolePermissions.AnyAsync())
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
            db.RolePermissions.AddRange(rolePerms);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} role-permissions.", rolePerms.Length);
        }

        // 4. WalletProviders
        if (!await db.WalletProviders.AnyAsync())
        {
            db.WalletProviders.AddRange(
                new WalletProvider("WAVE", "Wave Money", null, 1, id: 1),
                new WalletProvider("KBZPAY", "KBZPay", null, 2, id: 2));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 2 wallet providers.");
        }

        // 5. AdjustmentTypes
        if (!await db.AdjustmentTypes.AnyAsync())
        {
            db.AdjustmentTypes.AddRange(
                new AdjustmentType(AdjustmentType.CashCorrectionId, "CashCorrection", "Cash Correction"),
                new AdjustmentType(AdjustmentType.FloatTopUpId, "FloatTopUp", "Float Top-up"),
                new AdjustmentType(AdjustmentType.FloatWithdrawalId, "FloatWithdrawal", "Float Withdrawal"));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 3 adjustment types.");
        }

        // 6. TransactionTypes
        if (!await db.TransactionTypeSeeds.AnyAsync())
        {
            db.TransactionTypeSeeds.AddRange(
                new TransactionTypeSeed(1, "CashIn", "Cash In"),
                new TransactionTypeSeed(2, "CashOut", "Cash Out"));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 2 transaction types.");
        }

        // 7. TransactionStatuses
        if (!await db.TransactionStatusSeeds.AnyAsync())
        {
            db.TransactionStatusSeeds.AddRange(
                new TransactionStatusSeed(1, "Pending", "Pending"),
                new TransactionStatusSeed(2, "Completed", "Completed"),
                new TransactionStatusSeed(3, "Cancelled", "Cancelled"),
                new TransactionStatusSeed(4, "Reversed", "Reversed"));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 4 transaction statuses.");
        }

        // 8. AppSettings
        if (!await db.AppSettings.AnyAsync())
        {
            db.AppSettings.AddRange(
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
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 10 app settings.");
        }

        // 9. PhysicalCashAccount (single global pool)
        if (!await db.PhysicalCashAccounts.AnyAsync())
        {
            db.PhysicalCashAccounts.Add(
                Domain.Entities.PhysicalCashAccount.CreateForShop(0, 0, clock));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded physical cash account.");
        }
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
}
