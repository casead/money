using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MoneyRecord.API.Services;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Application.Transactions.Queries;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// TXN-001…005 (API-007 §7). Financial-critical creates: Idempotency-Key mandatory,
/// rate-limited 30/min/user, replay header on idempotent retries.
/// </summary>
[ApiController]
[Route("transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly ISender _sender;

    public TransactionsController(ISender sender) => _sender = sender;

    /// <summary>TXN-001 — Cash In (txn.create). Header Idempotency-Key (UUID v4) required.</summary>
    [HttpPost("cash-in")]
    [Authorize(Policy = Permissions.TxnCreate)] // SEC-003: server-side catalog check (blocks SuperAdmin)
    [EnableRateLimiting("txn-create")]           // SEC-006: 30/min/user
    public async Task<IActionResult> CreateCashIn([FromBody] TxnCreateRequest body,
        CancellationToken ct)
    {
        var (ok, key) = ParseIdempotencyKey();
        if (!ok)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency-Key header (UUID) လိုအပ်ပါသည်။");

        var result = await _sender.Send(new CreateCashInCommand
        {
            IdempotencyKey = key,
            CustomerId = body.CustomerId,
            CustomerName = body.CustomerName,
            CustomerPhone = body.CustomerPhone,
            WalletAccountId = body.WalletAccountId,
            Amount = body.Amount,
            FeeAmountOverride = body.FeeAmountOverride,
            FeeOverrideReason = body.FeeOverrideReason,
            FeePaidVia = body.FeePaidVia,
            Note = body.Note
        }, ct);

        if (!result.IsSuccess)
            return ApiProblem.From(result, HttpContext);

        if (result.Value!.IsReplay)
            Response.Headers["X-Idempotent-Replay"] = "true";

        return CreatedAtAction(nameof(GetByTxnNo), new { txnNo = result.Value.TxnNo },
            new { data = result.Value });
    }

    /// <summary>TXN-002 — Cash Out (cash-first lock order; BR-032 cash sufficiency).</summary>
    [HttpPost("cash-out")]
    [Authorize(Policy = Permissions.TxnCreate)] // SEC-003: server-side catalog check (blocks SuperAdmin)
    [EnableRateLimiting("txn-create")]           // SEC-006: 30/min/user
    public async Task<IActionResult> CreateCashOut([FromBody] TxnCreateRequest body,
        CancellationToken ct)
    {
        var (ok, key) = ParseIdempotencyKey();
        if (!ok)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency-Key header (UUID) လိုအပ်ပါသည်။");

        var result = await _sender.Send(new CreateCashOutCommand
        {
            IdempotencyKey = key,
            CustomerId = body.CustomerId,
            CustomerName = body.CustomerName,
            CustomerPhone = body.CustomerPhone,
            WalletAccountId = body.WalletAccountId,
            Amount = body.Amount,
            FeeAmountOverride = body.FeeAmountOverride,
            FeeOverrideReason = body.FeeOverrideReason,
            FeePaidVia = body.FeePaidVia,
            Note = body.Note
        }, ct);

        if (!result.IsSuccess)
            return ApiProblem.From(result, HttpContext);

        if (result.Value!.IsReplay)
            Response.Headers["X-Idempotent-Replay"] = "true";

        return CreatedAtAction(nameof(GetByTxnNo), new { txnNo = result.Value.TxnNo },
            new { data = result.Value });
    }

    /// <summary>TXN-003 — immutable detail incl. correction chain placeholders.</summary>
    [HttpGet("{txnNo}")]
    public async Task<IActionResult> GetByTxnNo(string txnNo, CancellationToken ct)
    {
        var result = await _sender.Send(new GetTransactionQuery(txnNo), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : ApiProblem.From(result, HttpContext);
    }

    /// <summary>TXN-004 — quick search (txnNo/phone/name/amount) ≤50 rows.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        var result = await _sender.Send(new SearchTransactionsQuery(q ?? string.Empty), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : ApiProblem.From(result, HttpContext);
    }

    /// <summary>TXN-005 — filtered paginated list; default today (Yangon).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null, [FromQuery] int? providerId = null,
        [FromQuery] long? walletAccountId = null, [FromQuery] byte? typeId = null,
        [FromQuery] byte? statusId = null, [FromQuery] long? createdByUserId = null,
        [FromQuery] long? minAmount = null, [FromQuery] long? maxAmount = null,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListTransactionsQuery(
            page, pageSize, dateFrom, dateTo, providerId, walletAccountId,
            typeId, statusId, createdByUserId, minAmount, maxAmount, sortBy, sortDir), ct);
        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);
        return Ok(new
        {
            data = result.Value.Items,
            pagination = new
            {
                page = result.Value.Pagination.Page,
                pageSize = result.Value.Pagination.PageSize,
                totalItems = result.Value.Pagination.TotalItems,
                totalPages = result.Value.Pagination.TotalPages
            }
        });
    }

    /// <summary>
    /// TXN-006 — Cancel same-day txn (txn.cancel, Admin-only). Cross-day txns are
    /// rejected with 409 + errorCode CONFLICT_STATE + reason=USE_REVERSAL extension.
    /// </summary>
    [HttpPost("{txnNo}/cancel")]
    [Authorize(Policy = Permissions.TxnCancel)]
    public async Task<IActionResult> Cancel(string txnNo, [FromBody] TxnCorrectionRequest body,
        CancellationToken ct)
    {
        Guid? idemKey = null;
        if (Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) &&
            Guid.TryParse(rawKey, out var parsed) && parsed != Guid.Empty)
            idemKey = parsed;

        var result = await _sender.Send(new CancelTransactionCommand(
            txnNo, body.Reason, idemKey), ct);

        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);
        return Ok(new { data = result.Value });
    }

    /// <summary>TXN-007 — Reverse via mirror txn (txn.reverse, Admin-only). Returns 201.</summary>
    [HttpPost("{txnNo}/reverse")]
    [Authorize(Policy = Permissions.TxnReverse)]
    public async Task<IActionResult> Reverse(string txnNo, [FromBody] TxnReversalRequest body,
        CancellationToken ct)
    {
        Guid? idemKey = null;
        if (Request.Headers.TryGetValue("Idempotency-Key", out var rawKey) &&
            Guid.TryParse(rawKey, out var parsed) && parsed != Guid.Empty)
            idemKey = parsed;

        var result = await _sender.Send(new ReverseTransactionCommand(
            txnNo, body.Reason, body.EffectiveDate, idemKey), ct);

        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);
        return CreatedAtAction(nameof(GetByTxnNo), new { txnNo = result.Value.ReversalTxnNo },
            new { data = result.Value });
    }

    // ---- helpers ----

    private (bool Ok, Guid Key) ParseIdempotencyKey()
    {
        if (Request.Headers.TryGetValue("Idempotency-Key", out var raw) &&
            Guid.TryParse(raw, out var key) && key != Guid.Empty)
            return (true, key);
        return (false, default);
    }

    public sealed record TxnCreateRequest(
        long? CustomerId,
        string CustomerName,
        string CustomerPhone,
        long WalletAccountId,
        long Amount,
        long? FeeAmountOverride,
        string? FeeOverrideReason,
        string FeePaidVia,
        string? Note);

    public sealed record TxnCorrectionRequest(string Reason);

    public sealed record TxnReversalRequest(string Reason, DateOnly? EffectiveDate);
}
