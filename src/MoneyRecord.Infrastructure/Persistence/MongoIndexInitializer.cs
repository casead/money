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
        // Scale-readiness batch (2026-09-30) — list/search default sort + indexed search branches.
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.ShopId),
                Builders<Transaction>.IndexKeys.Descending(t => t.OccurredAtUtc)),
            new CreateIndexOptions { Name = "IX_Transactions_ShopId_OccurredAt" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.ShopId),
                Builders<Transaction>.IndexKeys.Ascending(t => t.CustomerPhoneSnapshot)),
            new CreateIndexOptions { Name = "IX_Transactions_ShopId_Phone" }));
        await transactions.Indexes.CreateOneAsync(new CreateIndexModel<Transaction>(
            Builders<Transaction>.IndexKeys.Combine(
                Builders<Transaction>.IndexKeys.Ascending(t => t.ShopId),
                Builders<Transaction>.IndexKeys.Ascending(t => t.Amount)),
            new CreateIndexOptions { Name = "IX_Transactions_ShopId_Amount" }));

        // CashLedgerEntries indexes
        var cashLedger = database.GetCollection<CashLedgerEntry>("cashLedgerEntries");
        await cashLedger.Indexes.CreateOneAsync(new CreateIndexModel<CashLedgerEntry>(
            Builders<CashLedgerEntry>.IndexKeys.Ascending(e => e.CreatedAtUtc),
            new CreateIndexOptions { Name = "IX_CashLedger_CreatedAt" }));
        // Paged cash history: filter = CreatedByUserId IN (shop users) + date range (no ShopId field on ledgers).
        await cashLedger.Indexes.CreateOneAsync(new CreateIndexModel<CashLedgerEntry>(
            Builders<CashLedgerEntry>.IndexKeys.Combine(
                Builders<CashLedgerEntry>.IndexKeys.Ascending(e => e.CreatedByUserId),
                Builders<CashLedgerEntry>.IndexKeys.Descending(e => e.CreatedAtUtc)),
            new CreateIndexOptions { Name = "IX_CashLedger_CreatedBy_CreatedAt" }));

        // AuditLogs indexes (was `_id`-only in initializer)
        var auditLogs = database.GetCollection<AuditLog>("auditLogs");
        await auditLogs.Indexes.CreateOneAsync(new CreateIndexModel<AuditLog>(
            Builders<AuditLog>.IndexKeys.Combine(
                Builders<AuditLog>.IndexKeys.Ascending(a => a.ShopId),
                Builders<AuditLog>.IndexKeys.Descending(a => a.Id)),
            new CreateIndexOptions { Name = "IX_AuditLogs_ShopId_IdDesc" }));
        await auditLogs.Indexes.CreateOneAsync(new CreateIndexModel<AuditLog>(
            Builders<AuditLog>.IndexKeys.Combine(
                Builders<AuditLog>.IndexKeys.Ascending(a => a.ShopId),
                Builders<AuditLog>.IndexKeys.Ascending(a => a.CreatedAtUtc)),
            new CreateIndexOptions { Name = "IX_AuditLogs_ShopId_CreatedAt" }));
        // Audit retention (TTL): logging stays ON (security/compliance trail —
        // cancel/reverse, auth events, settings), but rows older than 180 days are
        // purged automatically so the collection cannot outgrow the Atlas tier —
        // space is bounded WITHOUT disabling the trail. To change the window,
        // drop IX_AuditLogs_CreatedAt_TTL first (same-name index with different
        // options = IndexOptionsConflict).
        await auditLogs.Indexes.CreateOneAsync(new CreateIndexModel<AuditLog>(
            Builders<AuditLog>.IndexKeys.Ascending(a => a.CreatedAtUtc),
            new CreateIndexOptions
            {
                Name = "IX_AuditLogs_CreatedAt_TTL",
                ExpireAfter = TimeSpan.FromDays(180)
            }));

        // WalletLedgerEntries indexes
        var walletLedger = database.GetCollection<WalletLedgerEntry>("walletLedgerEntries");
        await walletLedger.Indexes.CreateOneAsync(new CreateIndexModel<WalletLedgerEntry>(
            Builders<WalletLedgerEntry>.IndexKeys.Combine(
                Builders<WalletLedgerEntry>.IndexKeys.Ascending(e => e.WalletAccountId),
                Builders<WalletLedgerEntry>.IndexKeys.Ascending(e => e.CreatedAtUtc)),
            new CreateIndexOptions { Name = "IX_WalletLedger_AccountId_CreatedAt" }));

        // IdempotencyLeases indexes (used by MongoIdempotencyStore) — TTL matches prod
        // (expireAfterSeconds:0; same-name index with different options = IndexOptionsConflict).
        var idempotencyLeases = database.GetCollection<MongoIdempotencyStore.IdempotencyKeyDoc>("idempotencyLeases");
        await idempotencyLeases.Indexes.CreateOneAsync(new CreateIndexModel<MongoIdempotencyStore.IdempotencyKeyDoc>(
            Builders<MongoIdempotencyStore.IdempotencyKeyDoc>.IndexKeys.Ascending(k => k.ExpiresAtUtc),
            new CreateIndexOptions { Name = "IX_IdempotencyLeases_ExpiresAt", ExpireAfter = TimeSpan.Zero }));

        // RefreshTokens indexes
        var refreshTokens = database.GetCollection<RefreshToken>("refreshTokens");
        await refreshTokens.Indexes.CreateOneAsync(new CreateIndexModel<RefreshToken>(
            Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
            new CreateIndexOptions { Unique = true, Name = "UQ_RefreshTokens_TokenHash" }));
        // Session auto-expiry (TTL) — matches prod; revoked-but-unexpired tokens kept for reuse detection.
        await refreshTokens.Indexes.CreateOneAsync(new CreateIndexModel<RefreshToken>(
            Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAtUtc),
            new CreateIndexOptions { Name = "IX_RefreshTokens_ExpiresAt", ExpireAfter = TimeSpan.Zero }));

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

        // Seed rows carry fixed ids the value generators never saw (appSettings 1-10,
        // walletProviders 1-2): a fresh DB starts counters at 0, so the first
        // shop-override/provider insert would E11000-duplicate the seeded _id.
        await SyncCounterAsync(database, "appSettings", "AppSetting_id");
        await SyncCounterAsync(database, "walletProviders", "WalletProvider_id");

        Console.WriteLine("[MongoDB] Indexes created successfully.");
    }

    /// <summary>Raises a generator counter to the collection's current max _id (never lowers it).</summary>
    private static async Task SyncCounterAsync(IMongoDatabase database,
        string collectionName, string counterName)
    {
        var docs = database.GetCollection<MongoDB.Bson.BsonDocument>(collectionName);
        var top = await docs.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Empty)
            .SortByDescending(d => d["_id"])
            .Limit(1)
            .FirstOrDefaultAsync();
        if (top is null || !top["_id"].IsNumeric)
            return;

        long maxId = top["_id"] is MongoDB.Bson.BsonInt32 i32 ? i32.Value : top["_id"].AsInt64;

        var counters = database.GetCollection<MongoDB.Bson.BsonDocument>("counters");
        var filter = Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", counterName);
        var current = await counters.Find(filter).FirstOrDefaultAsync();
        var seq = current is not null && current.TryGetValue("seq", out var s) ? s.ToInt64() : 0L;
        if (maxId <= seq)
            return;

        await counters.UpdateOneAsync(filter,
            Builders<MongoDB.Bson.BsonDocument>.Update.Set("seq", maxId),
            new UpdateOptions { IsUpsert = true });
    }
}
