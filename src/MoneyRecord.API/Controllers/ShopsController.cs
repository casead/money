using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Shops.Commands;
using MoneyRecord.Application.Shops.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// TEN-001…005 (M11 multi-tenancy). All actions require tenant.manage —
/// SuperAdmin only (platform-level catalog permission).
/// </summary>
[ApiController]
[Route("shops")]
[Authorize(Policy = Permissions.TenantManage)]
public class ShopsController : ControllerBase
{
    private readonly ISender _sender;

    public ShopsController(ISender sender) => _sender = sender;

    /// <summary>TEN-004 — list shops (search/status filters).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] int? status = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListShopsQuery(page, pageSize, search, status), ct);
        return result.IsSuccess
            ? Ok(Envelope(result.Value.Items,
                new { page = result.Value.Pagination.Page, pageSize = result.Value.Pagination.PageSize,
                    totalItems = result.Value.Pagination.TotalItems,
                    totalPages = result.Value.Pagination.TotalPages }))
            : Error(result);
    }

    /// <summary>TEN-005 — shop details.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetShopQuery(id), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>Shop detail: users + transaction counts (day/week/month).</summary>
    [HttpGet("{id:long}/detail")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetShopDetailQuery(id), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>TEN-001 — create shop (unique Code).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShopRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(new CreateShopCommand(body.Code, body.Name), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, Envelope(result.Value))
            : Error(result);
    }

    /// <summary>TEN-002 — rename shop.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Rename(long id, [FromBody] RenameShopRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new UpdateShopCommand(id, body.Name), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>TEN-003 — suspend/reactivate. Suspended blocks all member logins (login gate).</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> SetStatus(long id, [FromBody] ShopStatusRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new SetShopStatusCommand(id, body.IsActive), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    // ---- helpers ----

    private static object Envelope<T>(T data) => new { data };

    private object Envelope<T>(T data, object pagination) =>
        new { data, pagination, traceId = HttpContext.TraceIdentifier };

    private ActionResult Error(Result result) => result.ErrorCode switch
    {
        Domain.Common.Errors.ErrorCodes.NotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.ErrorMessage ?? "NOT_FOUND"),
        Domain.Common.Errors.ErrorCodes.Duplicate => Conflict(new
        {
            title = result.ErrorMessage ?? "DUPLICATE",
            status = StatusCodes.Status409Conflict,
            errorCode = result.ErrorCode
        }),
        Domain.Common.Errors.ErrorCodes.Forbidden => Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: result.ErrorMessage ?? "FORBIDDEN"),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: result.ErrorMessage ?? result.ErrorCode ?? "ERROR")
    };

    public sealed record CreateShopRequest(string Code, string Name);
    public sealed record RenameShopRequest(string Name);
    public sealed record ShopStatusRequest(bool IsActive);
}
