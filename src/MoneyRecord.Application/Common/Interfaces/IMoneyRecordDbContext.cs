using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Read/write-side abstraction over persistence for Application handlers.
/// Backed by MoneyRecordDbContext in Infrastructure (ARCH-006 dependency rule).
/// Exposes what handlers need: entity sets, SaveChanges, and transaction control.
/// </summary>
public interface IMoneyRecordDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Shop> Shops { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Customer> Customers { get; }
    DbSet<WalletProvider> WalletProviders { get; }
    DbSet<WalletAccount> WalletAccounts { get; }
    DbSet<PhysicalCashAccount> PhysicalCashAccounts { get; }
    DbSet<CashLedgerEntry> CashLedgerEntries { get; }
    DbSet<WalletLedgerEntry> WalletLedgerEntries { get; }
    DbSet<AdjustmentType> AdjustmentTypes { get; }
    DbSet<CashAdjustment> CashAdjustments { get; }
    DbSet<FloatAdjustment> FloatAdjustments { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<TransactionCancellation> TransactionCancellations { get; }
    DbSet<TransactionReversal> TransactionReversals { get; }
    DbSet<FeeRule> FeeRules { get; }
    DbSet<CommissionSource> CommissionSources { get; }
    DbSet<CommissionEntry> CommissionEntries { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
    DbSet<AppSetting> AppSettings { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops all tracked entities (used by TransactionBehavior between retry attempts).</summary>
    Task ClearTrackedEntitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reloads an entity's current DB values (used under UPDLOCK to defeat staleness).</summary>
    Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : class;
}

/// <summary>Audit trail writer. Implementations append to AuditLogs within the current transaction.</summary>
public interface IAuditLogger
{
    Task LogAsync(string actionCode, string entityType, string entityId,
        string? oldValue = null, string? newValue = null,
        CancellationToken ct = default);
}

/// <summary>HTTP request context info for audit rows (IP, device).</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? DeviceInfo { get; }
}


