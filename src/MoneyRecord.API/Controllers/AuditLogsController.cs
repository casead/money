using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.API.Services;
using MoneyRecord.Application.Audit.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// AUD-001 (API-007 §11). Read-only by design — no update/delete endpoints exist (DR-04).
/// Admin only via audit.view policy.
/// </summary>
[ApiController]
[Route("audit-logs")]
[Authorize(Policy = Permissions.AuditView)]
public class AuditLogsController : ControllerBase
{
    private readonly ISender _sender;

    public AuditLogsController(ISender sender) => _sender = sender;

    /// <summary>AUD-001 — filtered, paginated audit trail.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? entityType = null, [FromQuery] string? entityId = null,
        [FromQuery] string? action = null, [FromQuery] long? actorUserId = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListAuditLogsQuery(
            page, pageSize, dateFrom, dateTo, entityType, entityId, action,
            actorUserId), ct);

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
            : ApiProblem.From(result, HttpContext);
    }
}
