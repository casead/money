using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// IBalanceLocker implementation for MongoDB.
/// Reads balances within the ambient MongoDB transaction — no explicit locks needed.
/// MongoDB transaction isolation (snapshot) ensures consistent reads within the transaction.
/// Callers MUST follow the fixed lock order (cash-in: wallet→cash, cash-out: cash→wallet).
/// </summary>
public sealed class MongoBalanceLocker : IBalanceLocker
{
    private readonly MoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<MongoBalanceLocker> _logger;

    public MongoBalanceLocker(MoneyRecordDbContext db, ICurrentUser currentUser,
        ILogger<MongoBalanceLocker> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<LockedCashBalance> LockPhysicalCashAsync(CancellationToken ct)
    {
        var shopId = (int)(_currentUser.ShopId
            ?? throw new InvalidOperationException("Shop context မရှိပါ။"));

        var cash = await _db.PhysicalCashAccounts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == shopId, ct);

        if (cash is null)
        {
            _logger.LogWarning("PhysicalCashAccount not found for shop {ShopId}, treating as 0", shopId);
            return new LockedCashBalance(shopId, 0);
        }

        _logger.LogDebug("Locked cash balance for shop {ShopId}: {Balance}", shopId, cash.CurrentCashBalance);
        return new LockedCashBalance(shopId, cash.CurrentCashBalance);
    }

    public async Task<LockedWalletBalance> LockWalletAccountAsync(long walletAccountId, CancellationToken ct)
    {
        var account = await _db.WalletAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == walletAccountId && !a.IsDeleted, ct);

        if (account is null)
            throw new NotFoundException("WalletAccount", walletAccountId);

        _logger.LogDebug("Locked wallet balance for account {Id}: {Balance}", account.Id, account.CurrentFloatBalance);
        return new LockedWalletBalance(account.Id, account.CurrentFloatBalance);
    }

    public async Task<long> LockTransactionRowAsync(long transactionId, CancellationToken ct)
    {
        var txn = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

        if (txn is null)
            throw new NotFoundException("Transaction", transactionId);

        return txn.Id;
    }
}
