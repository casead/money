using MoneyRecord.Infrastructure.Persistence;

namespace MoneyRecord.Worker;

/// <summary>
/// Daily reconciliation job (ARCH §7 / DR-01/02/08): runs at 02:00 Yangon time.
/// Compares cached balances vs Σ(signed ledger) and verifies BalanceAfter chains.
/// Clean run → stamps LastReconciledAtUtc. Drift/gaps → CRITICAL log alert.
/// Interval configurable: "Reconciliation:IntervalMinutes" (default = daily schedule).
/// </summary>
public sealed class ReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan YangonOffset = TimeSpan.FromHours(6.5);
    private const int TargetHourYangon = 2; // 02:00 MMT daily

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReconciliationWorker> _logger;
    private readonly IConfiguration _config;

    public ReconciliationWorker(IServiceScopeFactory scopeFactory,
        ILogger<ReconciliationWorker> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reconciliation worker started (daily {Hour:00}:00 Yangon).",
            TargetHourYangon);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextRun(DateTime.UtcNow);
                _logger.LogInformation("Next reconciliation at {Local:yyyy-MM-dd HH:mm} Yangon (in {Hours:F1} h).",
                    DateTime.UtcNow.Add(delay).Add(YangonOffset), delay.TotalHours);
                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never kill the host on a failed pass; retry in the next cycle window.
                _logger.LogError(ex, "Reconciliation run failed — will retry next cycle.");
            }
        }
    }

    /// <summary>One manual/on-demand pass — also used by tests and admin tooling.</summary>
    public async Task<ReconciliationResult> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ReconciliationService>();

        var result = await svc.RunAsync(ct);

        if (result.IsClean)
        {
            _logger.LogInformation(
                "Reconciliation CLEAN at {At:O}: all balances match Σledger, zero chain gaps.",
                result.RanAtUtc);
        }
        else
        {
            foreach (var drift in result.Drifts)
            {
                _logger.LogCritical(
                    "RECONCILIATION DRIFT [{Scope}]: cache={Cache:N0}, Σledger={Ledger:N0}, diff={Diff:N0}",
                    drift.Scope, drift.CachedBalance, drift.LedgerSignedSum, drift.Difference);
            }
            if (result.ChainGapCount > 0)
            {
                _logger.LogCritical("RECONCILIATION CHAIN GAPS: {Count} BalanceAfter discontinuities.",
                    result.ChainGapCount);
            }
        }

        return result;
    }

    private TimeSpan TimeUntilNextRun(DateTime utcNow)
    {
        // Test/dev override for fast cycles.
        if (_config.GetValue<int?>("Reconciliation:IntervalMinutes") is { } minutes &&
            minutes > 0 && !IsProduction())
        {
            return TimeSpan.FromMinutes(minutes);
        }

        var yangonNow = utcNow.Add(YangonOffset);
        var nextRun = yangonNow.Date.AddHours(TargetHourYangon);
        if (yangonNow >= nextRun)
            nextRun = nextRun.AddDays(1);
        return nextRun - yangonNow;
    }

    private bool IsProduction() =>
        string.Equals(_config["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);
}
