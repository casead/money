using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Balances.Queries;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.API.Services;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>BAL-001â€¦004 (API-007 Â§8). BAL-003 adjustments: Admin (balance.adjust), T4 boundary.</summary>
[ApiController]
[Route("balances")]
public class BalancesController : ControllerBase
{
    private readonly ISender _sender;

    public BalancesController(ISender sender) => _sender = sender;

    /// <summary>BAL-001 â€” physical cash balance + integrity flag.</summary>
    [HttpGet("cash")]
    [Authorize]
    public async Task<IActionResult> CashBalance(CancellationToken ct)
    {
        var result = await _sender.Send(new GetCashBalanceQuery(), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>BAL-002 â€” all wallet balances + totalFloat aggregate.</summary>
    [HttpGet("wallets")]
    [Authorize]
    public async Task<IActionResult> WalletBalances(CancellationToken ct)
    {
        var result = await _sender.Send(new GetWalletBalancesQuery(), ct);
        return result.IsSuccess
            ? Ok(new { data = result.Value.Accounts, totalFloat = result.Value.TotalFloat })
            : Error(result);
    }

    /// <summary>BAL-003 â€” adjust cash (scope=cash). Admin only, audited in-txn (T4).</summary>
    [HttpPost("cash/adjustments")]
    [Authorize(Policy = Permissions.BalanceAdjust)]
    public async Task<IActionResult> AdjustCash([FromBody] AdjustRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new AdjustBalanceCommand(
            "cash", null, body.Direction ?? "INCREASE", body.Amount ?? 0,
            body.Reason, body.CountedValue), ct);
        if (!result.IsSuccess) return Error(result);
        return Created("", new { data = result.Value });
    }

    /// <summary>BAL-003 â€” adjust a wallet account's float. Admin only, T4.</summary>
    [HttpPost("wallet/{walletAccountId:long}/adjustments")]
    [Authorize(Policy = Permissions.BalanceAdjust)]
    public async Task<IActionResult> AdjustWallet(long walletAccountId,
        [FromBody] AdjustRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(new AdjustBalanceCommand(
            "wallet", walletAccountId, body.Direction ?? "INCREASE", body.Amount ?? 0,
            body.Reason, body.CountedValue), ct);
        if (!result.IsSuccess) return Error(result);
        return Created("", new { data = result.Value });
    }

    /// <summary>BAL-004 â€” cash ledger history with BalanceAfter chain.</summary>
    [HttpGet("cash/history")]
    [Authorize]
    public async Task<IActionResult> CashHistory([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new GetCashLedgerQuery(page, pageSize, dateFrom, dateTo), ct);
        return result.IsSuccess
            ? Ok(new
            {
                data = result.Value.Items,
                pagination = new
                {
                    page = result.Value.Pagination.Page,
                    pageSize = result.Value.Pagination.PageSize,
                    totalItems = result.Value.Pagination.TotalItems,
                    totalPages = result.Value.Pagination.TotalPages
                }
            })
            : Error(result);
    }

    private ActionResult Error(Result result) => ApiProblem.From(result, HttpContext);

    /// <summary>Manual SET mode: send only countedValue (+reason); direction/amount optional.</summary>
    public sealed record AdjustRequest(
        string? Direction, long? Amount, string? Reason, long? CountedValue);
}


