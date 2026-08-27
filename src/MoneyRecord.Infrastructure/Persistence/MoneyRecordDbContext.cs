﻿using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Application DbContext. Acts as the Unit of Work via SaveChangesAsync (ARCH-006 Â§11).
/// Entity sets added module by module (M2: identity + audit).
/// </summary>
public class MoneyRecordDbContext : DbContext
{
    private readonly IClock? _clock;

    public MoneyRecordDbContext(DbContextOptions<MoneyRecordDbContext> options, IClock? clock = null)
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
        // Global: all string columns default to proper unicode handling (Burmese data)
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(string) && p.GetColumnType() is null))
        {
            property.SetIsUnicode(true);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MoneyRecordDbContext).Assembly);

        // Race-free TXN-YYYY-##### numbering source (M6 engine)
        modelBuilder.HasSequence<long>("TxnNoSeq").StartsAt(1).IncrementsBy(1);

        base.OnModelCreating(modelBuilder);
    }
}


