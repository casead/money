using MongoDB.Driver;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Initializes MongoDB indexes on application startup.
/// Uses MongoDB driver directly (not EF Core) for index creation.
/// </summary>
public static class MongoIndexInitializer
{
    public static async Task InitializeAsync(IMongoDatabase database)
    {

        // Users indexes
        var users = database.GetCollection<User>("users");
        await users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Username),
            new CreateIndexOptions { Unique = true, Name = "UQ_Users_Username" }));
        await users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.ShopId),
            new CreateIndexOptions { Name = "IX_Users_ShopId" }));

        // Customers indexes
        var customers = database.GetCollection<Customer>("customers");
        await customers.Indexes.CreateOneAsync(new CreateIndexModel<Customer>(
            Builders<Customer>.IndexKeys.Combine(
                Builders<Customer>.IndexKeys.Ascending(c => c.ShopId),
                Builders<Customer>.IndexKeys.Ascending(c => c.Phone)),
            new CreateIndexOptions { Name = "UQ_Customers_Shop_Phone" }));
        await customers.Indexes.CreateOneAsync(new CreateIndexModel<Customer>(
            Builders<Customer>.IndexKeys.Ascending(c => c.ShopId),
            new CreateIndexOptions { Name = "IX_Customers_ShopId" }));

        // WalletAccounts indexes
        var walletAccounts = database.GetCollection<WalletAccount>("walletAccounts");
        await walletAccounts.Indexes.CreateOneAsync(new CreateIndexModel<WalletAccount>(
            Builders<WalletAccount>.IndexKeys.Combine(
                Builders<WalletAccount>.IndexKeys.Ascending(a => a.WalletProviderId),
                Builders<WalletAccount>.IndexKeys.Ascending(a => a.AccountNumber)),
            new CreateIndexOptions { Name = "UQ_WalletAccounts_Provider_AccountNumber" }));
        await walletAccounts.Indexes.CreateOneAsync(new CreateIndexModel<WalletAccount>(
            Builders<WalletAccount>.IndexKeys.Ascending(a => a.ShopId),
            new CreateIndexOptions { Name = "IX_WalletAccounts_ShopId" }));

        // Transactions indexes
        var transactions = database.GetCollection<Transaction>("transactions");
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Ascending(t => t.TxnNo),
            new CreateIndexOptions { Unique = true, Name = "UQ_Transactions_TxnNo" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Ascending(t => t.IdempotencyKey),
            new CreateIndexOptions { Unique = true, Name = "UQ_Transactions_IdempotencyKey" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.ShopId),
                Builders<Transaction>.IndexKeys.Ascending(t => t.BusinessDate)),
            new CreateIndexOptions { Name = "IX_Transactions_ShopId_BusinessDate" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.BusinessDate),
                Builders<Transaction>.IndexKeys.Ascending(t => t.Type),
                Builders<Transaction>.IndexKeys.Ascending(t => t.Status)),
            new CreateIndexOptions { Name = "IX_Transactions_BusinessDate_Type_Status" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.CustomerId),
                Builders<Transaction>.IndexKeys.Ascending(t => t.BusinessDate)),
            new CreateIndexOptions { Name = "IX_Transactions_CustomerId_BusinessDate" }));

        // CashLedgerEntries indexes
        var cashLedger = database.GetCollection<CashLedgerEntry>("cashLedgerEntries");
        await cashLedger.Indexes.CreateOneAsync(new CreateIndexModel<CashLedgerEntry>(
            Builders<CashLedgerEntry>.IndexKeys.Ascending(e => e.CreatedAtUtc),
            new CreateIndexOptions { Name = "IX_CashLedger_CreatedAt" }));

        // WalletLedgerEntries indexes
        var walletLedger = database.GetCollection<WalletLedgerEntry>("walletLedgerEntries");
        await walletLedger.Indexes.CreateOneAsync(new CreateIndexModel<WalletLedgerEntry>(
            Builders<WalletLedgerEntry>.IndexKeys.Combine(
                Builders<WalletLedgerEntry>.IndexKeys.Ascending(e => e.WalletAccountId),
                Builders<WalletLedgerEntry>.IndexKeys.Ascending(e => e.CreatedAtUtc)),
            new CreateIndexOptions { Name = "IX_WalletLedger_AccountId_CreatedAt" }));

        // IdempotencyLeases indexes (used by MongoIdempotencyStore)
        var idempotencyLeases = database.GetCollection<MongoIdempotencyStore.IdempotencyKeyDoc>("idempotencyLeases");
        await idempotencyLeases.Indexes.CreateOneAsync(new CreateIndexModel<MongoIdempotencyStore.IdempotencyKeyDoc>(
            Builders<MongoIdempotencyStore.IdempotencyKeyDoc>.IndexKeys.Ascending(k => k.ExpiresAtUtc),
            new CreateIndexOptions { Name = "IX_IdempotencyLeases_ExpiresAt" }));

        // RefreshTokens indexes
        var refreshTokens = database.GetCollection<RefreshToken>("refreshTokens");
        await refreshTokens.Indexes.CreateOneAsync(new CreateIndexModel<RefreshToken>(
            Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
            new CreateIndexOptions { Unique = true, Name = "UQ_RefreshTokens_TokenHash" }));

        // FeeRules indexes
        var feeRules = database.GetCollection<FeeRule>("feeRules");
        await feeRules.Indexes.CreateOneAsync(new CreateIndexModel<FeeRule>(
            Builders<FeeRule>.IndexKeys.Combine(
                Builders<FeeRule>.IndexKeys.Ascending(r => r.WalletProviderId),
                Builders<FeeRule>.IndexKeys.Descending(r => r.EffectiveFromUtc)),
            new CreateIndexOptions { Name = "IX_FeeRules_Provider_EffectiveFrom" }));

        // WalletProviders indexes
        var walletProviders = database.GetCollection<WalletProvider>("walletProviders");
        await walletProviders.Indexes.CreateOneAsync(new CreateIndexModel<WalletProvider>(
            Builders<WalletProvider>.IndexKeys.Ascending(p => p.Code),
            new CreateIndexOptions { Unique = true, Name = "UQ_WalletProviders_Code" }));

        // Shops indexes
        var shops = database.GetCollection<Shop>("shops");
        await shops.Indexes.CreateOneAsync(new CreateIndexModel<Shop>(
            Builders<Shop>.IndexKeys.Ascending(s => s.Code),
            new CreateIndexOptions { Unique = true, Name = "UQ_Shops_Code" }));

        // AppSettings indexes
        var appSettings = database.GetCollection<AppSetting>("appSettings");
        await appSettings.Indexes.CreateOneAsync(new CreateIndexModel<AppSetting>(
            Builders<AppSetting>.IndexKeys.Combine(
                Builders<AppSetting>.IndexKeys.Ascending(s => s.Key),
                Builders<AppSetting>.IndexKeys.Ascending(s => s.ShopId)),
            new CreateIndexOptions { Unique = true, Name = "UQ_AppSettings_Key_Shop" }));

        // Roles indexes
        var roles = database.GetCollection<Role>("roles");
        await roles.Indexes.CreateOneAsync(new CreateIndexModel<Role>(
            Builders<Role>.IndexKeys.Ascending(r => r.Code),
            new CreateIndexOptions { Unique = true, Name = "UQ_Roles_Code" }));

        // Permissions indexes
        var permissions = database.GetCollection<Permission>("permissions");
        await permissions.Indexes.CreateOneAsync(new CreateIndexModel<Permission>(
            Builders<Permission>.IndexKeys.Ascending(p => p.Code),
            new CreateIndexOptions { Unique = true, Name = "UQ_Permissions_Code" }));

        // Counters collection (for TxnNumberGenerator) — _id is already unique in MongoDB
        var counters = database.GetCollection<MongoTxnNumberGenerator.CounterDocument>("counters");

        Console.WriteLine("[MongoDB] Indexes created successfully.");
    }
}
