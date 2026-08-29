using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Customers.Commands;
using MoneyRecord.Application.Customers.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>
/// CUS-001…006 (API-007 §4). Reads: Admin + Staff. Create: both.
/// Edit (CUS-004): Admin-only per SRS §5 matrix (Staff create ✓ / edit ✗).
/// </summary>
[ApiController]
[Route("customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ISender _sender;

    public CustomersController(ISender sender) => _sender = sender;

    /// <summary>CUS-001 — list/typeahead (search + pageSize=10 serves CUS-005).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] string? search = null, [FromQuery] bool includeDeleted = false,
        [FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? source = null, [FromQuery] bool? bookmarked = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new ListCustomersQuery(page, pageSize, sortBy, sortDir, search,
                dateFrom, dateTo, includeDeleted, source, bookmarked), ct);

        return result.IsSuccess
            ? Ok(Envelope(result.Value.Items,
                new { page = result.Value.Pagination.Page,
                    pageSize = result.Value.Pagination.PageSize,
                    totalItems = result.Value.Pagination.TotalItems,
                    totalPages = result.Value.Pagination.TotalPages }))
            : Error(result);
    }

    /// <summary>CUS-002 — create customer (A, S). 409 DUPLICATE carries existingCustomerId.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.CustomerManage)] // SEC-003: blocks SuperAdmin from shop operations
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new CreateCustomerCommand(body.FullName, body.Phone, body.Address, body.Note, body.Source),
            ct);

        if (!result.IsSuccess) return Error(result);
        return CreatedAtAction(nameof(GetDetails), new { id = result.Value.Id },
            Envelope(result.Value));
    }

    /// <summary>CUS-003 — details + lifetime aggregates.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetails(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetCustomerDetailsQuery(id), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>CUS-004 — update profile (Shop Admin only per SRS §5; Staff create ✓ / edit ✗).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "2")] // 2 = Shop Admin (SuperAdmin/Staff excluded by design)
    public async Task<IActionResult> Update(long id, [FromBody] UpdateCustomerRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new UpdateCustomerCommand(id, body.FullName, body.Phone, body.Address, body.Note),
            ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>CUS-006 — customer transaction history (rows land with M6 engine).</summary>
    [HttpGet("{id:long}/transactions")]
    public async Task<IActionResult> History(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null, [FromQuery] int? typeId = null,
        [FromQuery] int? statusId = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListCustomerTransactionsQuery(
            id, page, pageSize, dateFrom, dateTo, typeId, statusId), ct);
        return result.IsSuccess
            ? Ok(Envelope(result.Value.Items,
                new { page = result.Value.Pagination.Page,
                    pageSize = result.Value.Pagination.PageSize,
                    totalItems = result.Value.Pagination.TotalItems,
                    totalPages = result.Value.Pagination.TotalPages }))
            : Error(result);
    }

    /// <summary>Toggle bookmark for quick access in Bookmark tab.</summary>
    [HttpPost("{id:long}/bookmark")]
    public async Task<IActionResult> ToggleBookmark(long id, [FromBody] BookmarkRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new ToggleBookmarkCommand(id, body.IsBookmarked), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    // ---- helpers ----

    private static object Envelope<T>(T data) => new { data };
    private object Envelope<T>(T data, object pagination) =>
        new { data, pagination, traceId = HttpContext.TraceIdentifier };

    private ActionResult Error(Result result)
    {
        var status = result.ErrorCode switch
        {
            Domain.Common.Errors.ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            Domain.Common.Errors.ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            Domain.Common.Errors.ErrorCodes.Duplicate => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = $"https://api.moneyrecord.mm/errors/{(result.ErrorCode ?? "error").ToLowerInvariant()}",
            Title = result.ErrorMessage ?? result.ErrorCode ?? "ERROR",
            Status = status,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["errorCode"] = result.ErrorCode;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        if (result.Extensions is not null)
            foreach (var (key, value) in result.Extensions)
                problem.Extensions[key] = value;

        return new ObjectResult(problem) { StatusCode = status };
    }

    public sealed record CreateCustomerRequest(
        string FullName, string Phone, string? Address, string? Note, string? Source);

    public sealed record UpdateCustomerRequest(
        string? FullName, string? Phone, string? Address, string? Note);

    public sealed record BookmarkRequest(bool IsBookmarked);
}
