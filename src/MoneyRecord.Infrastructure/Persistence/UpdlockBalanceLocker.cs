using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// IBalanceLocker implementation (ARCH §10 / BRL §19):
/// raw-SQL FOR UPDATE row locks inside the ambient TxBehavior transaction,
/// lock_timeout 5s → PostgresException 55P03 maps to 409 LOCK_TIMEOUT (BR-035).
/// Callers enforce fixed ordering: cash-in = wallet→cash, cash-out = cash→wallet.
/// </summary>
public sealed class UpdlockBalanceLocker : IBalanceLocker
{
    private readonly MoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly Microsoft.Extensions.Logging.ILogger<UpdlockBalanceLocker> _logger;

    public UpdlockBalanceLocker(MoneyRecordDbContext db, ICurrentUser currentUser,
        Microsoft.Extensions.Logging.ILogger<UpdlockBalanceLocker> logger)
    { _db = db; _currentUser = currentUser; _logger = logger; }

    public async Task<LockedCashBalance> LockPhysicalCashAsync(CancellationToken ct)
    {
        await SetLockTimeoutAsync(ct);
        try
        {
            var shopId = (int)(_currentUser.ShopId
                ?? throw new InvalidOperationException("Shop context မရှိပါ။"));
            var rows = await _db.Database
                .SqlQuery<long>($@"
                    SELECT ""CurrentCashBalance"" AS ""Value""
                    FROM ""PhysicalCashAccounts""
                    WHERE ""Id"" = {shopId}
                    FOR UPDATE")
                .ToListAsync(ct);
            // Row absent (legacy shop) → treat as 0; adjust path self-heals the row.
            return new LockedCashBalance(shopId, rows.Count == 0 ? 0 : rows[0]);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new LockTimeoutException();
        }
    }

    public async Task<LockedWalletBalance> LockWalletAccountAsync(long walletAccountId, CancellationToken ct)
    {
        await SetLockTimeoutAsync(ct);
        try
        {
            var rows = await _db.Database
                .SqlQuery<LockedWalletRow>($@"
                    SELECT ""Id"" AS ""Id"", ""CurrentFloatBalance"" AS ""Balance"",
                           txid_current() AS ""Trancount""
                    FROM ""WalletAccounts""
                    WHERE ""Id"" = {walletAccountId} AND ""IsDeleted"" = false
                    FOR UPDATE")
                .ToListAsync(ct);

            if (rows.Count == 0)
                throw new NotFoundException("WalletAccount", walletAccountId);

            var row = rows[0];
            _logger.LogWarning("TXNDBG locker: wallet {Id} balance={Bal} TXID={Tc} hasTx={HasTx}",
                row.Id, row.Balance, row.Trancount, _db.Database.CurrentTransaction != null);
            return new LockedWalletBalance(row.Id, row.Balance);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new LockTimeoutException();
        }
    }

    /// <summary>EC-03: serializes concurrent cancel/reverse on the same txn row.</summary>
    public async Task<long> LockTransactionRowAsync(long transactionId, CancellationToken ct)
    {
        await SetLockTimeoutAsync(ct);
        try
        {
            return await _db.Database
                .SqlQuery<long>($@"
                    SELECT ""Id"" AS ""Value""
                    FROM ""Transactions""
                    WHERE ""Id"" = {transactionId}
                    FOR UPDATE")
                .SingleAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new LockTimeoutException();
        }
    }

    /// <summary>
    /// Applies to the current transaction only (true = local to tx); scoped per statement.
    /// </summary>
    private async Task SetLockTimeoutAsync(CancellationToken ct)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('lock_timeout', '5000', false)";
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class LockedWalletRow
    {
        public long Id { get; set; }
        public long Balance { get; set; }
        public long Trancount { get; set; }
    }
}
