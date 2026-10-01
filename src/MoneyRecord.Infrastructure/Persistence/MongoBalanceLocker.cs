using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// IBalanceLocker implementation for MongoDB: a process-wide keyed mutex
/// (BalanceLockRegistry) serializes the read-modify-write critical section per
/// balance row — without it, concurrent handlers each mutate their own tracked
/// copy and the last SaveChanges wins (lost update). Snapshot reads are
/// AsNoTracking; callers MUST follow the fixed lock order (cash → wallet) and
/// dispose the lease AFTER SaveChanges.
/// </summary>
public sealed class MongoBalanceLocker : IBalanceLocker
{
    private readonly MoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly BalanceLockRegistry _registry;
    private readonly ILogger<MongoBalanceLocker> _logger;

    public MongoBalanceLocker(MoneyRecordDbContext db, ICurrentUser currentUser,
        BalanceLockRegistry registry, ILogger<MongoBalanceLocker> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _registry = registry;
        _logger = logger;
    }

    public async Task<LockedCashBalance> LockPhysicalCashAsync(CancellationToken ct)
    {
        var shopId = (int)(_currentUser.ShopId
            ?? throw new InvalidOperationException("Shop context မရှိပါ။"));
        var key = $"cash:{shopId}";

        await _registry.AcquireAsync(key, ct);
        try
        {
            var cash = await _db.PhysicalCashAccounts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == shopId, ct);

            if (cash is null)
            {
                _logger.LogWarning("PhysicalCashAccount not found for shop {ShopId}, treating as 0", shopId);
                return new LockedCashBalance(shopId, 0, () => _registry.Release(key));
            }

            _logger.LogDebug("Locked cash balance for shop {ShopId}: {Balance}", shopId, cash.CurrentCashBalance);
            return new LockedCashBalance(shopId, cash.CurrentCashBalance, () => _registry.Release(key));
        }
        catch
        {
            _registry.Release(key);
            throw;
        }
    }

    public async Task<LockedWalletBalance> LockWalletAccountAsync(long walletAccountId, CancellationToken ct)
    {
        var key = $"wallet:{walletAccountId}";

        await _registry.AcquireAsync(key, ct);
        try
        {
            var account = await _db.WalletAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == walletAccountId && !a.IsDeleted, ct);

            if (account is null)
                throw new NotFoundException("WalletAccount", walletAccountId);

            _logger.LogDebug("Locked wallet balance for account {Id}: {Balance}", account.Id, account.CurrentFloatBalance);
            return new LockedWalletBalance(account.Id, account.CurrentFloatBalance, () => _registry.Release(key));
        }
        catch
        {
            _registry.Release(key);
            throw;
        }
    }

    public async Task<LockedTransactionRow> LockTransactionRowAsync(long transactionId, CancellationToken ct)
    {
        var key = $"txn:{transactionId}";

        await _registry.AcquireAsync(key, ct);
        try
        {
            var txn = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

            if (txn is null)
                throw new NotFoundException("Transaction", transactionId);

            return new LockedTransactionRow(txn.Id, () => _registry.Release(key));
        }
        catch
        {
            _registry.Release(key);
            throw;
        }
    }
}
