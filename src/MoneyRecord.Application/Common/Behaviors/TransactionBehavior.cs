using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Common.Behaviors;

/// <summary>
/// Wraps command handlers in a single database transaction (user rule #7:
/// DB transactions for financial operations; ARCH-006 §15 TxBehavior).
/// Queries (IRequest&lt;T&gt; not marked as commands) pass through unwrapped.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IMoneyRecordDbContext db,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Skip for queries: any request NOT implementing ICommand marker.
        if (request is not ICommand)
            return await next();

        const int maxAttempts = 3;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // EF Core execution strategy handles transient retries around the whole transaction
                // when EnableRetryOnFailure is configured — use it explicitly to avoid nested-strategy errors.
                var response = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        var result = await next();
                        await _db.SaveChangesAsync(cancellationToken);
                        await tx.CommitAsync(cancellationToken);
                        return result;
                    }
                    catch
                    {
                        await tx.RollbackAsync(cancellationToken);
                        throw;
                    }
                });

                return response!;
            }
            catch (DbUpdateException ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                // Attempt-N tracked entities (idempotency reservation, txn, ledger rows)
                // must not leak into the next attempt — clear stale state.
                await _db.ClearTrackedEntitiesAsync(cancellationToken);
                _logger.LogWarning(ex, "Transient DB failure on {Request}, retrying ({Attempt}/{Max}).",
                    typeof(TRequest).Name, attempt, maxAttempts);
            }
            catch
            {
                // Terminal failure: the transaction is already rolled back; purge tracked
                // entities so a scope reused for another request never inherits stale state.
                await _db.ClearTrackedEntitiesAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private static bool IsTransient(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Marker for state-changing requests that must run inside a transaction.</summary>
public interface ICommand
{
}
