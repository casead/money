using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>Adapts MoneyRecordDbContext to the Application-level persistence interface.</summary>
public sealed class MoneyRecordDbContextAdapter : IMoneyRecordDbContext
{
    private readonly MoneyRecordDbContext _db;

    public MoneyRecordDbContextAdapter(MoneyRecordDbContext db) => _db = db;

    public DbSet<User> Users => _db.Users;
    public DbSet<Role> Roles => _db.Roles;
    public DbSet<Shop> Shops => _db.Shops;
    public DbSet<Permission> Permissions => _db.Permissions;
    public DbSet<RolePermission> RolePermissions => _db.RolePermissions;
    public DbSet<RefreshToken> RefreshTokens => _db.RefreshTokens;
    public DbSet<AuditLog> AuditLogs => _db.AuditLogs;
    public DbSet<Customer> Customers => _db.Customers;
    public DbSet<WalletProvider> WalletProviders => _db.WalletProviders;
    public DbSet<WalletAccount> WalletAccounts => _db.WalletAccounts;
    public DbSet<PhysicalCashAccount> PhysicalCashAccounts => _db.PhysicalCashAccounts;
    public DbSet<CashLedgerEntry> CashLedgerEntries => _db.CashLedgerEntries;
    public DbSet<WalletLedgerEntry> WalletLedgerEntries => _db.WalletLedgerEntries;
    public DbSet<AdjustmentType> AdjustmentTypes => _db.AdjustmentTypes;
    public DbSet<CashAdjustment> CashAdjustments => _db.CashAdjustments;
    public DbSet<FloatAdjustment> FloatAdjustments => _db.FloatAdjustments;
    public DbSet<Transaction> Transactions => _db.Transactions;
    public DbSet<TransactionCancellation> TransactionCancellations => _db.TransactionCancellations;
    public DbSet<TransactionReversal> TransactionReversals => _db.TransactionReversals;
    public DbSet<FeeRule> FeeRules => _db.FeeRules;
    public DbSet<CommissionSource> CommissionSources => _db.CommissionSources;
    public DbSet<CommissionEntry> CommissionEntries => _db.CommissionEntries;
    public DbSet<IdempotencyKey> IdempotencyKeys => _db.IdempotencyKeys;
    public DbSet<AppSetting> AppSettings => _db.AppSettings;

    public DatabaseFacade Database => _db.Database;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public Task ClearTrackedEntitiesAsync(CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    public async Task ReloadAsync<TEntity>(TEntity entity,
        CancellationToken cancellationToken = default) where TEntity : class
        => await _db.Entry(entity).ReloadAsync(cancellationToken);
}

/// <summary>Appends audit rows using the same DbContext (same transaction) as the handler.</summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly ICurrentUser? _currentUser;
    private readonly IClock _clock;

    public AuditLogger(IMoneyRecordDbContext db, IRequestContext requestContext,
        ICurrentUser? currentUser, IClock clock)
    {
        _db = db;
        _requestContext = requestContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task LogAsync(string actionCode, string entityType, string entityId,
        string? oldValue = null, string? newValue = null,
        CancellationToken ct = default)
    {
        _db.AuditLogs.Add(AuditLog.Create(
            actionCode, entityType, entityId,
            oldValuesJson: oldValue, newValuesJson: newValue,
            ipAddress: _requestContext.IpAddress,
            deviceInfo: _requestContext.DeviceInfo,
            actorUserId: _currentUser?.UserId,
            clock: _clock,
            shopId: _currentUser?.ShopId));
        // Saved by the handler's/behavior's SaveChangesAsync to keep the command atomic.
        return Task.CompletedTask;
    }
}




