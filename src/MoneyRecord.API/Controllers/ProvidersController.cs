using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Balances.Queries;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.API.Services;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>PRV-001...005 (API-007 §5). Reads: A+S; writes: provider.manage (Admin).</summary>
[ApiController]
[Route("providers")]
public class ProvidersController : ControllerBase
{
    private readonly ISender _sender;

    public ProvidersController(ISender sender) => _sender = sender;

    /// <summary>PRV-001 — list providers w/ account counts + total float.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListProvidersQuery(includeInactive), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>PRV-002 — create provider (provider.manage). Audit PROVIDER.CREATE.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.ProviderManage)]
    public async Task<IActionResult> Create([FromBody] CreateProviderRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new CreateProviderCommand(body.Code, body.Name, body.LogoUrl, body.DisplayOrder),
            ct);
        if (!result.IsSuccess) return Error(result);
        return CreatedAtAction(nameof(List), new { id = result.Value.Id },
            new { data = result.Value });
    }

    /// <summary>PRV-003 — update provider (code immutable).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ProviderManage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProviderRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new UpdateProviderCommand(id, body.Name, body.LogoUrl, body.DisplayOrder), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>PRV-004 — activate/deactivate (blocks new txns when inactive).</summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = Permissions.ProviderManage)]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new SetProviderStatusCommand(id, body.IsActive), ct);
        return result.IsSuccess ? Ok(new { data = result.Value }) : Error(result);
    }

    /// <summary>PRV-005 — soft-delete provider (must have no accounts).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.ProviderManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _sender.Send(new DeleteProviderCommand(id), ct);
        return result.IsSuccess ? Ok() : Error(result);
    }

    private ActionResult Error(Result result) => ApiProblem.From(result, HttpContext);

    public sealed record CreateProviderRequest(
        string Code, string Name, string? LogoUrl, int? DisplayOrder);

    public sealed record UpdateProviderRequest(string? Name, string? LogoUrl, int? DisplayOrder);

    public sealed record StatusRequest(bool IsActive);
}
