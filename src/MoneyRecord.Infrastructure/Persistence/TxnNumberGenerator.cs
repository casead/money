using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Native SEQUENCE-backed txn numbers (UQ_Transactions_TxnNo backstop remains).
/// Uses a raw DbCommand because nextval() is illegal inside the derived-table
/// wrapper that EF's composable SqlQuery generates.
/// </summary>
public sealed class TxnNumberGenerator : ITxnNumberGenerator
{
    private readonly MoneyRecordDbContext _db;

    public TxnNumberGenerator(MoneyRecordDbContext db) => _db = db;

    public async Task<long> NextAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT nextval('\"TxnNoSeq\"')";
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
