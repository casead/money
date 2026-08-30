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
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
            try
            {
                // MongoDB EF Core provider does not implement relational BeginTransactionAsync.
                // Only wrap in a transaction when the provider supports it (relational DBs).
                if (_db.Database.ProviderName != "MongoDB.EntityFrameworkCore")
                {
                    tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                }

                var result = await next();
                await _db.SaveChangesAsync(cancellationToken);

                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);

                return result!;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                await _db.ClearTrackedEntitiesAsync(cancellationToken);
                _logger.LogWarning(ex, "Transient DB failure on {Request}, retrying ({Attempt}/{Max}).",
                    typeof(TRequest).Name, attempt, maxAttempts);
                await Task.Delay(100 * attempt, cancellationToken);
            }
            catch
            {
                if (tx is not null)
                {
                    try { await tx.RollbackAsync(CancellationToken.None); } catch { /* best effort */ }
                }
                await _db.ClearTrackedEntitiesAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (tx is not null)
                    await tx.DisposeAsync();
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("TransientTransactionError", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
        ex.InnerException is not null && IsTransient(ex.InnerException);
}

/// <summary>Marker for state-changing requests that must run inside a transaction.</summary>
public interface ICommand
{
}
