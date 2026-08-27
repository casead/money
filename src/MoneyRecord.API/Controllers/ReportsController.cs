using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.API.Services;
using MoneyRecord.Application.Reports.Queries;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// RPT-001…004 (API-007 §10). Profit fields Admin-only (report.profit / role check);
/// CSV export streams with UTF-8 BOM for Excel compatibility (Excel-MM).
/// </summary>
[ApiController]
[Route("reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ISender _sender;

    public ReportsController(ISender sender) => _sender = sender;

    /// <summary>RPT-001 — single-call dashboard aggregate (profit tiles Admin-only).</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var result = await _sender.Send(new GetDashboardQuery(date), ct);
        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);

        var v = result.Value!;
        return Ok(new
        {
            date = v.Date,
            cashBalance = v.CashBalance,
            totalFloat = v.TotalFloat,
            byProvider = v.ByProvider,
            todayCashInTotal = v.TodayCashInTotal,
            todayCashOutTotal = v.TodayCashOutTotal,
            todayTxnCount = v.TodayTxnCount,
            todayGrossProfit = v.TodayGrossProfit, // null for staff → key still present but valueless
            lowBalanceWarnings = v.LowBalanceWarnings
        });
    }

    /// <summary>RPT-002 — daily report; format=csv streams a file download.</summary>
    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] DateOnly? date,
        [FromQuery] string? groupBy, [FromQuery] string? format,
        CancellationToken ct)
    {
        var result = await _sender.Send(new GetDailyReportQuery(date, groupBy ?? "provider"), ct);
        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);

        return format == "csv"
            ? Csv($"daily-{result.Value!.Date:yyyy-MM-dd}.csv",
                DailyCsvRows(result.Value!))
            : Ok(new { data = result.Value });
    }

    /// <summary>RPT-003 — monthly aggregate over Yangon month boundaries.</summary>
    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly([FromQuery] int? year,
        [FromQuery] int? month, [FromQuery] string? format,
        CancellationToken ct)
    {
        var result = await _sender.Send(new GetMonthlyReportQuery(year, month), ct);
        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);

        return format == "csv"
            ? Csv($"monthly-{result.Value!.Period}.csv", MonthlyCsvRows(result.Value!))
            : Ok(new { data = result.Value });
    }

    /// <summary>RPT-004 — profit series. Admin only (report.profit).</summary>
    [HttpGet("profit")]
    [Authorize(Policy = Domain.Common.Rbac.Permissions.ReportProfit)]
    public async Task<IActionResult> Profit([FromQuery] DateOnly dateFrom,
        [FromQuery] DateOnly dateTo, [FromQuery] string dimension = "day",
        CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new GetProfitReportQuery(dateFrom, dateTo, dimension), ct);
        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : ApiProblem.From(result, HttpContext);
    }

    // ---- CSV streaming (BOM + quoting per TC-1000d) ----

    private static string Esc(string s) =>
        s.Contains('"') || s.Contains(',') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static string Ks(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> DailyCsvRows(DailyReportResponse r)
    {
        yield return "Provider,CashIn,CashOut,TxnCount,Fees,Commissions";
        foreach (var p in r.ByProvider)
            yield return $"{Esc(p.ProviderCode)},{Ks(p.CashInTotal)},{Ks(p.CashOutTotal)},{p.TxnCount},{Ks(p.Fees)},{Ks(p.Commissions)}";
        yield return $"TOTAL,{Ks(r.TotalCashIn)},{Ks(r.TotalCashOut)},{r.TxnCount},{Ks(r.Fees)},{Ks(r.Commissions)}";
    }

    private static IEnumerable<string> MonthlyCsvRows(MonthlyReportResponse r)
    {
        yield return "Period,Provider,CashIn,CashOut,TxnCount,Fees,Commissions,GrossProfit";
        foreach (var p in r.ByProvider)
            yield return $"{r.Period},{Esc(p.ProviderCode)},{Ks(p.CashInTotal)},{Ks(p.CashOutTotal)},{p.TxnCount},{Ks(p.Fees)},{Ks(p.Commissions)},{Ks(p.Fees - p.Commissions)}";
        yield return $"{r.Period},TOTAL,{Ks(r.TotalCashIn)},{Ks(r.TotalCashOut)},{r.TxnCount},{Ks(r.TotalFees)},{Ks(r.TotalCommissions)},{Ks(r.GrossProfit)}";
    }

    private FileContentResult Csv(string fileName, IEnumerable<string> lines)
    {
        // BOM first so Excel detects UTF-8 (Burmese-safe)
        var body = Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var withBom = new byte[Encoding.UTF8.GetPreamble().Length + body.Length];
        Encoding.UTF8.GetPreamble().CopyTo(withBom, 0);
        body.CopyTo(withBom, Encoding.UTF8.GetPreamble().Length);

        return File(withBom, "text/csv; charset=utf-8", fileName);
    }
}
