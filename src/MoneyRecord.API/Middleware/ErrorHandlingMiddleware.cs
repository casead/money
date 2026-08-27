using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MoneyRecord.Application.Common.Exceptions;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.API.Middleware;

/// <summary>
/// Global exception → RFC7807 mapping (API-007 §1.2). Never leaks internals.
/// </summary>
public static class ErrorHandlingMiddleware
{
    public static void UseErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
                var exception = exceptionFeature?.Error;

                var (status, errorCode, title, errors) = Map(exception);

                context.Response.StatusCode = status;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Type = $"https://api.moneyrecord.mm/errors/{errorCode.ToLowerInvariant()}",
                    Title = title,
                    Status = status,
                    Instance = context.Request.Path
                };

                // traceId correlation: response ↔ logs ↔ audit (ARCH-006 §18)
                if (context.Items.TryGetValue("TraceId", out var traceId))
                    problem.Extensions["traceId"] = traceId;
                problem.Extensions["errorCode"] = errorCode;

                if (errors is not null)
                    problem.Extensions["errors"] = errors;

                await context.Response.WriteAsJsonAsync(problem);
            });
        });
    }

    private static (int Status, string ErrorCode, string Title, IDictionary<string, string[]>? Errors) Map(
        Exception? exception)
    {
        return exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "Validation failed.", ve.Errors),
            NotFoundException nf => (StatusCodes.Status404NotFound, ErrorCodes.NotFound, nf.Message, null),
            ConflictStateException cs => (StatusCodes.Status409Conflict, ErrorCodes.ConflictState, cs.Message, null),
            LockTimeoutException => (StatusCodes.Status409Conflict, ErrorCodes.LockTimeout,
                "Balance lock အချိန်ကုန်သွားပါသည် — ခဏနေပြီး ပြန်ကြိုးစားပါ။", null),
            DuplicateRequestException => (StatusCodes.Status409Conflict, ErrorCodes.DuplicateRequest,
                "Idempotency-Key တူညီသော်လည်း payload ကွာခြားနေပါသည်။", null),
            InsufficientForDecreaseException ifd => (
                StatusCodes.Status422UnprocessableEntity, ErrorCodes.InsufficientForDecrease,
                ifd.Message,
                new Dictionary<string, string[]> { ["currentBalance"] = [ifd.CurrentBalance.ToString()] }),
            InsufficientFloatException ife => (StatusCodes.Status422UnprocessableEntity, ErrorCodes.InsufficientFloat,
                ife.Message, new Dictionary<string, string[]> { ["currentBalance"] = [ife.CurrentBalance.ToString()] }),
            InsufficientCashException ice => (StatusCodes.Status422UnprocessableEntity, ErrorCodes.InsufficientCash,
                ice.Message, new Dictionary<string, string[]> { ["currentBalance"] = [ice.CurrentBalance.ToString()] }),
            // Auth-specific mappings (API-007 AUTH-001/003)
            Domain.Entities.AccountLockedException lo =>
                (StatusCodes.Status423Locked, lo.ErrorCode, lo.Message,
                    new Dictionary<string, string[]> { ["lockedUntilUtc"] = [lo.LockedUntilUtc.ToString("O")] }),
            Domain.Entities.RefreshTokenReuseException rr =>
                (StatusCodes.Status409Conflict, rr.ErrorCode, rr.Message, null),
            BusinessRuleException br => (StatusCodes.Status422UnprocessableEntity, br.ErrorCode, br.Message, null),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "ခွင့်ပြုချက် မရှိပါ။", null),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
                "စနစ်အတွင်းပိုင်း အမှားတစ်ခု ဖြစ်ပွားသည်။", null)
        };
    }
}
