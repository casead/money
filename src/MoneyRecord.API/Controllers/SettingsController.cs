using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.API.Services;
using MoneyRecord.Application.Settings.Commands;
using MoneyRecord.Application.Settings.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// SET-001/002 (API-007 §12). GET: both roles (staff sees safe keys only).
/// PUT: Admin only (settings.manage); sensitive keys need confirmSensitive.
/// </summary>
[ApiController]
[Route("settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly ISender _sender;

    public SettingsController(ISender sender) => _sender = sender;

    /// <summary>SET-001 — settings map (role-scoped visibility).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _sender.Send(new GetSettingsQuery(), ct);
        return result.IsSuccess
            ? Ok(new { data = result.Value.Values })
            : ApiProblem.From(result, HttpContext);
    }

    /// <summary>SET-002 — partial update. Sensitive change without confirm → 409.</summary>
    [HttpPut]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new UpdateSettingsCommand(body.Values, body.ConfirmSensitive), ct);

        return result.IsSuccess
            ? Ok(new { data = result.Value })
            : ApiProblem.From(result, HttpContext);
    }

    public sealed record UpdateSettingsRequest(
        Dictionary<string, string> Values, bool ConfirmSensitive = false);
}
