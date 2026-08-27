using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.API.Services;
using MoneyRecord.Application.Fees.Commands;
using MoneyRecord.Application.Fees.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// FEE-001…004 (API-007 §9). Rule management Admin-only (fee.manage);
/// list/preview readable by both roles.
/// </summary>
[ApiController]
[Route("fee-rules")]
[Authorize]
public class FeeRulesController : ControllerBase
{
    private readonly ISender _sender;

    public FeeRulesController(ISender sender) => _sender = sender;

    /// <summary>FEE-001 — list fee rules (providerId?, activeOnly?, asOfDate?).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? providerId,
        [FromQuery] bool activeOnly = false, [FromQuery] DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListFeeRulesQuery(providerId, activeOnly, asOfDate), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : ApiProblem.From(result, HttpContext);
    }

    /// <summary>FEE-002 — create rule; overlapping window → 409 OVERLAP_RULE.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.FeeManage)]
    public async Task<IActionResult> Create([FromBody] CreateFeeRuleRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(new CreateFeeRuleCommand(
            body.ProviderId, body.CalculationType, body.FlatFee, body.PercentRate,
            body.MinFee, body.MaxFee, body.EffectiveFrom), ct);

        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);
        return CreatedAtAction(nameof(List), new { id = result.Value!.Id },
            new { data = result.Value });
    }

    /// <summary>FEE-003 — update only NOT-YET-EFFECTIVE rules → else 409 IMMUTABLE_RULE.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.FeeManage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFeeRuleRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new UpdateFeeRuleCommand(
            id, body.FlatFee, body.PercentRate, body.MinFee, body.MaxFee,
            body.EffectiveFrom), ct);

        if (!result.IsSuccess) return ApiProblem.From(result, HttpContext);
        return Ok(new { data = result.Value });
    }

    /// <summary>FEE-004 — live fee preview (percent-only v2). type=cash-in|cash-out.</summary>
    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate([FromQuery] string type,
        [FromQuery] long amount, CancellationToken ct)
    {
        if (!TryParseTxnType(type, out var txnType))
            return ApiProblem.From(
                MoneyRecord.Application.Common.Models.Result.Failure(
                    Domain.Common.Errors.ErrorCodes.ValidationFailed,
                    "type သည် 'cash-in' သာမဟုတ် 'cash-out' သာ ဖြစ်ရမည်။"),
                HttpContext);

        var result = await _sender.Send(new PreviewFeeQuery(txnType, amount), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : ApiProblem.From(result, HttpContext);
    }

    private static bool TryParseTxnType(string? raw,
        out Domain.Entities.TransactionType txnType)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "cash-in":
            case "cashin":
            case "1":
                txnType = Domain.Entities.TransactionType.CashIn;
                return true;
            case "cash-out":
            case "cashout":
            case "2":
                txnType = Domain.Entities.TransactionType.CashOut;
                return true;
            default:
                txnType = default;
                return false;
        }
    }
}

public sealed record CreateFeeRuleRequest(
    int ProviderId,
    byte CalculationType,
    long? FlatFee,
    decimal? PercentRate,
    long? MinFee,
    long? MaxFee,
    DateOnly EffectiveFrom);

public sealed record UpdateFeeRuleRequest(
    long? FlatFee,
    decimal? PercentRate,
    long? MinFee,
    long? MaxFee,
    DateOnly? EffectiveFrom);
