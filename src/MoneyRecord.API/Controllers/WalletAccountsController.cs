using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Balances.Queries;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.API.Services;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>ACC-001â€¦003 (API-007 Â§6) + create extension for the setup wizard.</summary>
[ApiController]
[Route("wallet-accounts")]
public class WalletAccountsController : ControllerBase
{
    private readonly ISender _sender;

    public WalletAccountsController(ISender sender) => _sender = sender;

    /// <summary>ACC-001 â€” list accounts (providerId?, includeInactive?).</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List([FromQuery] int? providerId,
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new ListWalletAccountsQuery(providerId, includeInactive), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>Create wallet account w/ opening float — SHOP ADMIN only
    /// (the float is the shop's own declared money; platform role never
    /// touches shop balances). Account stamps actor's ShopId.</summary>
    [HttpPost]
    [Authorize(Roles = "2")] // 2 = ShopAdmin
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new CreateWalletAccountCommand(
            body.ProviderId, body.AccountName, body.AccountNumber, body.OpeningFloat), ct);
        if (!result.IsSuccess) return Error(result);
        return CreatedAtAction(nameof(GetBalance), new { id = result.Value.Id },
            new { data = result.Value });
    }

    /// <summary>ACC-002 â€” cached balance + ledgerSumVerified integrity flag.</summary>
    [HttpGet("{id:long}/balance")]
    [Authorize]
    public async Task<IActionResult> GetBalance(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetAccountBalanceQuery(id), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>ACC-003 â€” balance history (ledger DESC with BalanceAfter chain).</summary>
    [HttpGet("{id:long}/balance-history")]
    [Authorize]
    public async Task<IActionResult> History(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null, [FromQuery] int? sourceType = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetAccountLedgerQuery(
            id, page, pageSize, dateFrom, dateTo, sourceType), ct);
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

    /// <summary>Soft-delete a wallet account (Admin-only). Fails if balance != 0.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "1,2")] // 1=SuperAdmin, 2=ShopAdmin
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new DeleteWalletAccountCommand(id), ct);
        return result.IsSuccess ? Ok(new { data = (string?)null }) : Error(result);
    }

    private ActionResult Error(Result result) => ApiProblem.From(result, HttpContext);

    public sealed record CreateAccountRequest(
        int ProviderId, string AccountName, string? AccountNumber, long OpeningFloat);
}


