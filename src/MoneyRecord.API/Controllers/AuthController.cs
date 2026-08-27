using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;
using MoneyRecord.Application.Auth.Commands;
using MoneyRecord.Application.Auth.Queries;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Users.Commands;

namespace MoneyRecord.API.Controllers;

/// <summary>AUTH-001…004 (API-007 §2).</summary>
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender) => _sender = sender;

    /// <summary>AUTH-001 — Login. Rate-limited 5/min/IP (SEC-006).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new LoginCommand(body.Username, body.Password, body.DeviceInfo,
                body.TotpCode), ct);

        return result.IsSuccess
            ? Ok(Envelope(result.Value))
            : Error(result);
    }

    /// <summary>AUTH-003 — Refresh token rotation. No AT required.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new RefreshCommand(body.RefreshToken), ct);

        if (result.IsSuccess) return Ok(Envelope(result.Value));

        // Theft signal (AUTH-003 / TC-300b): 409 CONFLICT_STATE
        return result.ErrorCode == MoneyRecord.Domain.Common.Errors.ErrorCodes.ConflictState
            ? Conflict(new { title = result.ErrorMessage, status = StatusCodes.Status409Conflict })
            : Error(result);
    }

    /// <summary>AUTH-002 — Logout current device; idempotent.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest body, CancellationToken ct)
    {
        var result = await _sender.Send(new LogoutCommand(body.RefreshToken), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>AUTH-004 — Current user profile + permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var result = await _sender.Send(new GetCurrentUserQuery(), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>AUTH-005 — Self-service password change. Revokes all sessions on success.</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new ChangeMyPasswordCommand(body.CurrentPassword, body.NewPassword), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>SEC — MFA step 1: generate TOTP secret + otpauth:// URI (QR for authenticator app).</summary>
    [HttpPost("mfa/enroll")]
    [Authorize]
    public async Task<IActionResult> StartMfaEnrollment(CancellationToken ct)
    {
        var result = await _sender.Send(new StartMfaEnrollmentCommand(), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>SEC — MFA step 2: confirm with a live code from the authenticator.</summary>
    [HttpPost("mfa/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmMfaEnrollment([FromBody] MfaCodeRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new ConfirmMfaEnrollmentCommand(body.Code), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>SEC — disable MFA (current valid code required).</summary>
    [HttpDelete("mfa")]
    [Authorize]
    public async Task<IActionResult> DisableMfa([FromBody] MfaCodeRequest body,
        CancellationToken ct)
    {
        var result = await _sender.Send(new DisableMfaCommand(body.Code), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>SEC — active sessions (devices) of the current user.</summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var result = await _sender.Send(new ListMySessionsQuery(), ct);
        return result.IsSuccess ? Ok(Envelope(result.Value)) : Error(result);
    }

    /// <summary>SEC — revoke every session. keepCurrent=true spares this device's session.</summary>
    [HttpPost("sessions/revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessions(
        [FromBody] RevokeAllSessionsRequest? body, CancellationToken ct)
    {
        var result = await _sender.Send(
            new RevokeMySessionsCommand(body?.KeepCurrent ?? false), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    // ---- helpers ----

    private static object Envelope<T>(T data) => new { data };

    /// <summary>Maps Result.ErrorCode → HTTP status per API-007 §1.2 (401/423/403/409).
    /// Every problem body carries `errorCode` so clients can branch
    /// (e.g. MFA_REQUIRED → show TOTP field instead of "wrong password").</summary>
    private ActionResult Error(Result result)
    {
        var status = result.ErrorCode switch
        {
            "LOCKED_OUT" => StatusCodes.Status423Locked,
            Domain.Common.Errors.ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            Domain.Common.Errors.ErrorCodes.MfaRequired => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status401Unauthorized
        };
        var problem = Problem(
            statusCode: status,
            title: result.ErrorMessage ?? result.ErrorCode ?? "UNAUTHORIZED");
        if (problem is ObjectResult obj && obj.Value is ProblemDetails pd)
            pd.Extensions["errorCode"] = result.ErrorCode;
        return problem;
    }

    public sealed record LoginRequest(string Username, string Password,
        string? DeviceInfo, string? TotpCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
    public sealed record RevokeAllSessionsRequest(bool KeepCurrent = false);
}
