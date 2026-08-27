using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Users.Commands;
using MoneyRecord.Application.Users.Queries;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Controllers;

/// <summary>USR-001…005 (API-007 §3). USR-001/003 Admin-only; writes require user.manage.</summary>
[ApiController]
[Route("users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender) => _sender = sender;

    /// <summary>USR-001 — List users (SuperAdmin + Shop Admin). Paginated per API-007 §1.1/1.3.</summary>
    [HttpGet]
    [Authorize(Roles = "1,2")] // 1=SuperAdmin, 2=ShopAdmin — both manage users
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] bool? isActive = null, [FromQuery] string? search = null,
        [FromQuery] long? shopId = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new ListUsersQuery(page, pageSize, sortBy, sortDir, isActive, search, shopId), ct);

        return result.IsSuccess
            ? Ok(Envelope(result.Value.Items,
                new { page = result.Value.Pagination.Page, pageSize = result.Value.Pagination.PageSize,
                    totalItems = result.Value.Pagination.TotalItems,
                    totalPages = result.Value.Pagination.TotalPages }))
            : Error(result);
    }

    /// <summary>USR-002 — Create user (user.manage).</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.UserManage)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(
            new CreateUserCommand(body.Username, body.Password, body.FullName, body.Phone,
                body.RoleId, body.ShopId), ct);

        if (!result.IsSuccess) return Error(result);

        return CreatedAtAction(nameof(GetDetails), new { id = result.Value.Id },
            Envelope(result.Value));
    }

    /// <summary>USR-003 — Get user details (SuperAdmin + Shop Admin).</summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = "1,2")]
    public async Task<IActionResult> GetDetails(long id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetUserDetailsQuery(id), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>USR-004 — Update profile/role (user.manage). Username immutable.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = Permissions.UserManage)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new UpdateUserCommand(id, body.FullName, body.Phone, body.RoleId), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>USR-005 — Activate/deactivate (user.manage). Deactivate revokes all sessions.</summary>
    [HttpPatch("{id:long}/status")]
    [Authorize(Policy = Permissions.UserManage)]
    public async Task<IActionResult> SetStatus(long id, [FromBody] SetStatusRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new SetUserStatusCommand(id, body.IsActive), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>USR-006 — Reset a user's password (user.manage). Target's sessions revoked.</summary>
    [HttpPost("{id:long}/reset-password")]
    [Authorize(Policy = Permissions.UserManage)]
    public async Task<IActionResult> ResetPassword(long id,
        [FromBody] ResetPasswordRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(new ResetUserPasswordCommand(id, body.NewPassword), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    // ---- helpers ----

    private static object Envelope<T>(T data) => new { data };
    private object Envelope<T>(T data, object pagination) =>
        new { data, pagination, traceId = HttpContext.TraceIdentifier };

    /// <summary>Maps Result.ErrorCode → HTTP status per API-007 §1.2 (with traceId correlation).</summary>
    private ActionResult Error(Result result)
    {
        var status = result.ErrorCode switch
        {
            Domain.Common.Errors.ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            Domain.Common.Errors.ErrorCodes.Forbidden or
            Domain.Common.Errors.ErrorCodes.SelfRoleChange or
            Domain.Common.Errors.ErrorCodes.SelfDeactivate or
            Domain.Common.Errors.ErrorCodes.LastAdmin => StatusCodes.Status403Forbidden,
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

        return new ObjectResult(problem) { StatusCode = status };
    }

    public sealed record CreateUserRequest(
        string Username, string Password, string FullName, string? Phone, int RoleId,
        long? ShopId = null);

    public sealed record UpdateUserRequest(string? FullName, string? Phone, int? RoleId);

    public sealed record SetStatusRequest(bool IsActive);

    public sealed record ResetPasswordRequest(string NewPassword);
}
