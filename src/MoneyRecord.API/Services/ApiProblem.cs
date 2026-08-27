using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Common.Models;

namespace MoneyRecord.API.Services;

/// <summary>Shared Result → RFC7807 mapping with errorCode/traceId extensions (API-007 §1.2).</summary>
public static class ApiProblem
{
    public static ActionResult From(Result result, HttpContext http)
    {
        var status = result.ErrorCode switch
        {
            Domain.Common.Errors.ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            Domain.Common.Errors.ErrorCodes.Forbidden or
            Domain.Common.Errors.ErrorCodes.SelfRoleChange or
            Domain.Common.Errors.ErrorCodes.SelfDeactivate or
            Domain.Common.Errors.ErrorCodes.LastAdmin => StatusCodes.Status403Forbidden,
            Domain.Common.Errors.ErrorCodes.Duplicate => StatusCodes.Status409Conflict,
            Domain.Common.Errors.ErrorCodes.LockTimeout => StatusCodes.Status409Conflict,
            Domain.Common.Errors.ErrorCodes.InsufficientForDecrease or
            Domain.Common.Errors.ErrorCodes.InsufficientCash or
            Domain.Common.Errors.ErrorCodes.InsufficientFloat or
            Domain.Common.Errors.ErrorCodes.InvalidOperation => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Type = $"https://api.moneyrecord.mm/errors/{(result.ErrorCode ?? "error").ToLowerInvariant()}",
            Title = result.ErrorMessage ?? result.ErrorCode ?? "ERROR",
            Status = status,
            Instance = http.Request.Path
        };
        problem.Extensions["errorCode"] = result.ErrorCode;
        problem.Extensions["traceId"] = http.TraceIdentifier;
        if (result.Extensions is not null)
            foreach (var (key, value) in result.Extensions)
                problem.Extensions[key] = value;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
