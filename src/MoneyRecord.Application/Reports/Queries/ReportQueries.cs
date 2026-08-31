using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Common.Settings;
using MoneyRecord.Application.Transactions.Queries;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Reports.Queries;

/// <summary>
/// Correction-aware netting shared by all report surfaces (TC-800g reuse):
/// only COMPLETED transactions carry money movement; CANCELLED/REVERSED are excluded.
/// Shop-scoped via explicit ShopId filter (M11 — no global filter on Transactions).
/// </summary>
internal static class ReportNetting
{
    public static IQueryable<Transaction> Netted(IMoneyRecordDbContext db, DateOnly from,
        DateOnly to, long? shopId)
        => db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == shopId
                        && t.BusinessDate >= from && t.BusinessDate <= to
                        && t.Status == TransactionStatus.Completed);
}

// ---------------------------------------------------------------- RPT-001

/// <summary>RPT-001 — single-call dashboard aggregate for one BusinessDate.</summary>
public sealed record GetDashboardQuery(DateOnly? Date) : IRequest<Result<DashboardResponse>>;

public sealed record DashboardProviderRow(
    int ProviderId,
    string ProviderCode,
    long FloatBalance);

public sealed record DashboardResponse(
    DateOnly Date,
    long CashBalance,
    long TotalFloat,
    IReadOnlyList<DashboardProviderRow> ByProvider,
    long TodayCashInTotal,
    long TodayCashOutTotal,
    int TodayTxnCount,
    long? TodayGrossProfit,
    IReadOnlyList<string> LowBalanceWarnings);

public sealed class GetDashboardQueryHandler
    : IRequestHandler<GetDashboardQuery, Result<DashboardResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetDashboardQueryHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<DashboardResponse>> Handle(
        GetDashboardQuery request, CancellationToken ct)
    {
        var date = request.Date ?? _clock.TodayYangon;
        if (request.Date is { } d && (d.Year < 2024 || d.Year > 2100))
            return Result<DashboardResponse>.Failure(ErrorCodes.ValidationFailed,
                "date သည် 2024–2100 အတွင်း ရှိရမည်။");

        var showProfit = ProfitVisibility.ShowProfit(_currentUser);

        // Balance caches (live values, not date-scoped — balances are "now" state).
        // ShopId null (SuperAdmin without shop context) → zeros; never mix tenants.
        var cash = _currentUser.ShopId is null
            ? null
            : await _db.PhysicalCashAccounts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == _currentUser.ShopId, ct); // per-shop cash pool (M11)
        var accounts = _currentUser.ShopId is null
            ? []
            : await _db.WalletAccounts.AsNoTracking()
                .Where(a => a.IsActive && !a.IsDeleted && a.ShopId == _currentUser.ShopId)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

        var providerIds = accounts.Select(a => a.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var accountViews = accounts.Select(a => new
        {
            a.Id,
            ProviderId = a.WalletProviderId,
            ProviderCode = providers.TryGetValue(a.WalletProviderId, out var pc) ? pc : "???",
            a.CurrentFloatBalance
        }).ToList();

        // Day aggregates (netted: only COMPLETED rows) — load into memory for MongoDB compat.
        var nettedDay = ReportNetting.Netted(_db, date, date, _currentUser.ShopId);
        var dayRows = await nettedDay.ToListAsync(ct);
        var cashInTotal = dayRows.Where(t => t.Type == TransactionType.CashIn).Sum(t => t.Amount);
        var cashOutTotal = dayRows.Where(t => t.Type == TransactionType.CashOut).Sum(t => t.Amount);
        var dayTxnCount = dayRows.Count;
        var dayGross = showProfit
            ? dayRows.Sum(t => t.FeeAmount - t.CommissionAmount)
            : (long?)null;

        // Low-balance warnings from settings thresholds (shop-scoped w/ global fallback)
        var cashThreshold = await SettingReader.EffectiveIntAsync(
            _db, "lowBalanceCashThreshold", _currentUser.ShopId, ct);
        var floatThreshold = await SettingReader.EffectiveIntAsync(
            _db, "lowBalanceFloatThresholdPerAccount", _currentUser.ShopId, ct);

        var warnings = new List<string>();
        var cashBalance = cash?.CurrentCashBalance ?? 0;
        if (cash is not null && cash.CurrentCashBalance < cashThreshold)
            warnings.Add($"ရုံးငွေ လက်ကျန် {cash.CurrentCashBalance:N0} Ks သည် သတိပေးချက်အနိမ့် ({cashThreshold:N0} Ks) အောက် ရောက်နေပါသည်။");
        foreach (var a in accountViews.Where(a => a.CurrentFloatBalance < floatThreshold))
            warnings.Add($"{a.ProviderCode} float လက်ကျန် {a.CurrentFloatBalance:N0} Ks သည် သတိပေးချက်အနိမ့် ({floatThreshold:N0} Ks) အောက် ရောက်နေပါသည်။");

        return Result<DashboardResponse>.Success(new DashboardResponse(
            date,
            cashBalance,
            accountViews.Sum(a => a.CurrentFloatBalance),
            accountViews.Select(a => new DashboardProviderRow(
                a.ProviderId, a.ProviderCode, a.CurrentFloatBalance)).ToList(),
            cashInTotal,
            cashOutTotal,
            dayTxnCount,
            dayGross,
            warnings));
    }
}

// ---------------------------------------------------------------- RPT-002

/// <summary>RPT-002 — daily report grouped by provider (staff grouping reserved post-v1).</summary>
public sealed record GetDailyReportQuery(DateOnly? Date, string GroupBy)
    : IRequest<Result<DailyReportResponse>>;

public sealed record DailyReportProviderRow(
    string ProviderCode,
    long CashInTotal,
    long CashOutTotal,
    int TxnCount,
    long Fees,
    long Commissions);

public sealed record DailyReportResponse(
    DateOnly Date,
    long TotalCashIn,
    long TotalCashOut,
    int TxnCount,
    int CancellationCount,
    long Fees,
    long Commissions,
    long GrossProfit,
    IReadOnlyList<DailyReportProviderRow> ByProvider);

public sealed class GetDailyReportQueryValidator : AbstractValidator<GetDailyReportQuery>
{
    public GetDailyReportQueryValidator()
    {
        RuleFor(x => x.GroupBy)
            .Must(g => g is null || new[] { "provider", "staff" }.Contains(g))
            .WithMessage("groupBy သည် provider|staff သာ ဖြစ်ရမည်။");
    }
}

public sealed class GetDailyReportQueryHandler
    : IRequestHandler<GetDailyReportQuery, Result<DailyReportResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetDailyReportQueryHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<DailyReportResponse>> Handle(
        GetDailyReportQuery request, CancellationToken ct)
    {
        var date = request.Date ?? _clock.TodayYangon;
        var shopId = _currentUser.ShopId;

        // Load ALL transactions for the date+shop into memory (MongoDB compat).
        var allTxns = await _db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == shopId && t.BusinessDate == date)
            .ToListAsync(ct);

        var allRows = allTxns.Where(t => t.Status == TransactionStatus.Completed).ToList();

        var cashInTotal = allRows.Where(t => t.Type == TransactionType.CashIn).Sum(t => t.Amount);
        var cashOutTotal = allRows.Where(t => t.Type == TransactionType.CashOut).Sum(t => t.Amount);
        var fees = allRows.Sum(t => t.FeeAmount);
        var commissions = allRows.Sum(t => t.CommissionAmount);
        var txnCount = allRows.Count;

        var cancellations = allTxns.Count(t => t.Status == TransactionStatus.Cancelled);

        var providerIds = allRows.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var rowsWithCode = allRows.Select(t => new
        {
            t.Type, t.Amount, t.FeeAmount, t.CommissionAmount,
            Code = providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???"
        }).ToList();

        var byProvider = rowsWithCode
            .GroupBy(r => r.Code)
            .Select(g => new DailyReportProviderRow(
                g.Key,
                g.Where(r => r.Type == TransactionType.CashIn).Sum(r => r.Amount),
                g.Where(r => r.Type == TransactionType.CashOut).Sum(r => r.Amount),
                g.Count(),
                g.Sum(r => r.FeeAmount),
                g.Sum(r => r.CommissionAmount)))
            .OrderBy(r => r.ProviderCode)
            .ToList();

        return Result<DailyReportResponse>.Success(new DailyReportResponse(
            date, cashInTotal, cashOutTotal, txnCount, cancellations,
            fees, commissions, fees - commissions, byProvider));
    }
}

// ---------------------------------------------------------------- RPT-003

/// <summary>RPT-003 — monthly aggregate over Yangon month boundaries.</summary>
public sealed record GetMonthlyReportQuery(int? Year, int? Month)
    : IRequest<Result<MonthlyReportResponse>>;

public sealed record MonthlyReportResponse(
    string Period,
    long TotalCashIn,
    long TotalCashOut,
    long TotalFees,
    long TotalCommissions,
    long GrossProfit,
    int TxnCount,
    int CancellationCount,
    IReadOnlyList<DailyReportProviderRow> ByProvider);

public sealed class GetMonthlyReportQueryValidator : AbstractValidator<GetMonthlyReportQuery>
{
    public GetMonthlyReportQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2024, 2100)
            .When(x => x.Year is not null);
        RuleFor(x => x.Month).InclusiveBetween(1, 12)
            .When(x => x.Month is not null);
    }
}

public sealed class GetMonthlyReportQueryHandler
    : IRequestHandler<GetMonthlyReportQuery, Result<MonthlyReportResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetMonthlyReportQueryHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<MonthlyReportResponse>> Handle(
        GetMonthlyReportQuery request, CancellationToken ct)
    {
        var today = _clock.TodayYangon;
        var year = request.Year ?? today.Year;
        var month = request.Month ?? today.Month;

        if (year < 2024 || year > 2100)
            return Result<MonthlyReportResponse>.Failure(ErrorCodes.ValidationFailed,
                "year သည် 2024–2100 အတွင်း ရှိရမည်။");
        if (month < 1 || month > 12)
            return Result<MonthlyReportResponse>.Failure(ErrorCodes.ValidationFailed,
                "month သည် 1–12 အတွင်း ရှိရမည်။");

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var shopId = _currentUser.ShopId;

        // Load ALL transactions for the month+shop into memory (MongoDB compat).
        var allTxns = await _db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == shopId
                        && t.BusinessDate >= from && t.BusinessDate <= to)
            .ToListAsync(ct);

        var allRows = allTxns.Where(t => t.Status == TransactionStatus.Completed).ToList();

        var cashInTotal = allRows.Where(t => t.Type == TransactionType.CashIn).Sum(t => t.Amount);
        var cashOutTotal = allRows.Where(t => t.Type == TransactionType.CashOut).Sum(t => t.Amount);
        var fees = allRows.Sum(t => t.FeeAmount);
        var commissions = allRows.Sum(t => t.CommissionAmount);
        var txnCount = allRows.Count;

        var cancellations = allTxns.Count(t => t.Status == TransactionStatus.Cancelled);

        var providerIds = allRows.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var monthlyRowsWithCode = allRows.Select(t => new
        {
            t.Type, t.Amount, t.FeeAmount, t.CommissionAmount,
            Code = providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???"
        }).ToList();

        var byProvider = monthlyRowsWithCode
            .GroupBy(r => r.Code)
            .Select(g => new DailyReportProviderRow(
                g.Key,
                g.Where(r => r.Type == TransactionType.CashIn).Sum(r => r.Amount),
                g.Where(r => r.Type == TransactionType.CashOut).Sum(r => r.Amount),
                g.Count(),
                g.Sum(r => r.FeeAmount),
                g.Sum(r => r.CommissionAmount)))
            .OrderBy(r => r.ProviderCode)
            .ToList();

        return Result<MonthlyReportResponse>.Success(new MonthlyReportResponse(
            $"{year:D4}-{month:D2}",
            cashInTotal, cashOutTotal, fees, commissions,
            fees - commissions, txnCount, cancellations, byProvider));
    }
}

// ---------------------------------------------------------------- RPT-004

/// <summary>RPT-004 — profit series (Admin only): profit = fees − commissions per BR-016.</summary>
public sealed record GetProfitReportQuery(DateOnly DateFrom, DateOnly DateTo, string Dimension)
    : IRequest<Result<IReadOnlyList<ProfitReportRow>>>;

public sealed record ProfitReportRow(
    string Bucket,
    long Fees,
    long Commissions,
    long Profit);

public sealed class GetProfitReportQueryValidator : AbstractValidator<GetProfitReportQuery>
{
    private static readonly string[] Dimensions = ["day", "month", "provider", "type"];

    public GetProfitReportQueryValidator()
    {
        RuleFor(x => x.Dimension)
            .Must(d => Dimensions.Contains(d))
            .WithMessage("dimension သည် day|month|provider|type သာ ဖြစ်ရမည်။");
        RuleFor(x => x)
            .Must(x => x.DateTo.DayNumber - x.DateFrom.DayNumber <= 366)
            .WithMessage("ရက်အပိုင်းအခြားသည် ၃၆၆ ရက်ထက် မကျော်ရပါ။")
            .When(x => x.DateFrom != default && x.DateTo != default);
    }
}

public sealed class GetProfitReportQueryHandler
    : IRequestHandler<GetProfitReportQuery, Result<IReadOnlyList<ProfitReportRow>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetProfitReportQueryHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ProfitReportRow>>> Handle(
        GetProfitReportQuery request, CancellationToken ct)
    {
        var (from, to) = request.DateFrom <= request.DateTo
            ? (request.DateFrom, request.DateTo)
            : (request.DateTo, request.DateFrom);

        if (from.Year < 2024 || from.Year > 2100 || to.Year > 2100)
            return Result<IReadOnlyList<ProfitReportRow>>.Failure(
                ErrorCodes.ValidationFailed, "ရက်စွဲ အပိုင်းအခြား မှားယွင်းနေပါသည်။");

        if (!ProfitVisibility.ShowProfit(_currentUser))
            return Result<IReadOnlyList<ProfitReportRow>>.Failure(
                ErrorCodes.Forbidden, "Profit report ကို Admin များသာ ကြည့်နိုင်ပါသည်။");

        var shopId = _currentUser.ShopId;
        var netted = ReportNetting.Netted(_db, from, to, shopId);

        // Load full entities into memory for MongoDB compatibility.
        var allTxns = await netted.ToListAsync(ct);

        var providerIds = allTxns.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var rows = allTxns.Select(t => new
        {
            t.Type, t.BusinessDate, t.FeeAmount, t.CommissionAmount,
            Code = providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???"
        }).ToList();

        List<ProfitReportRow> series;
        switch (request.Dimension)
        {
            case "provider":
                series = rows
                    .GroupBy(r => r.Code)
                    .Select(g => new ProfitReportRow(
                        g.Key, g.Sum(r => r.FeeAmount),
                        g.Sum(r => r.CommissionAmount),
                        g.Sum(r => r.FeeAmount) - g.Sum(r => r.CommissionAmount)))
                    .OrderBy(r => r.Bucket).ToList();
                break;
            case "type":
                series = rows
                    .GroupBy(r => r.Type)
                    .Select(g => new ProfitReportRow(
                        g.Key.ToString(), g.Sum(r => r.FeeAmount),
                        g.Sum(r => r.CommissionAmount),
                        g.Sum(r => r.FeeAmount) - g.Sum(r => r.CommissionAmount)))
                    .OrderBy(r => r.Bucket).ToList();
                break;
            case "month":
                series = rows
                    .GroupBy(r => new { r.BusinessDate.Year, r.BusinessDate.Month })
                    .Select(g => new ProfitReportRow(
                        $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                        g.Sum(r => r.FeeAmount),
                        g.Sum(r => r.CommissionAmount),
                        g.Sum(r => r.FeeAmount) - g.Sum(r => r.CommissionAmount)))
                    .OrderBy(r => r.Bucket).ToList();
                break;
            default: // "day"
                series = rows
                    .GroupBy(r => r.BusinessDate)
                    .Select(g => new ProfitReportRow(
                        g.Key.ToString("yyyy-MM-dd"),
                        g.Sum(r => r.FeeAmount),
                        g.Sum(r => r.CommissionAmount),
                        g.Sum(r => r.FeeAmount) - g.Sum(r => r.CommissionAmount)))
                    .OrderBy(r => r.Bucket).ToList();
                break;
        }

        return Result<IReadOnlyList<ProfitReportRow>>.Success(series);
    }
}
