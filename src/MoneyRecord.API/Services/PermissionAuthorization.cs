using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.API.Services;

/// <summary>Authorization requirement asserting the caller's role holds a permission.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Resolves dynamic policies named after permission codes (e.g. Policy="user.manage").
/// Unknown names defer to base behavior (null → standard "policy undefined" failure).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null)
            return existing;

        return Permissions.IsKnown(policyName)
            ? new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build()
            : null;
    }
}

/// <summary>
/// FR-009 / SEC-003: server-side role→permission check against the v1 registry.
/// Denials are audited (AUTHZ.DENIED) per FR-009 AC-c / FR-037 "Permission Denied Attempts".
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IMoneyRecordDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ICurrentUser currentUser, IAuditLogger audit,
        IMoneyRecordDbContext db, IHttpContextAccessor http,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _currentUser = currentUser;
        _audit = audit;
        _db = db;
        _http = http;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // roleId comes from the JWT claim validated at authentication time
        var roleIdValue = context.User.FindFirstValue("roleId");
        var roleId = int.TryParse(roleIdValue, out var rid) ? rid : (int?)null;

        if (roleId is null || !RolePermissionRegistry.RoleHas(roleId.Value, requirement.Permission))
        {
            _logger.LogWarning(
                "Authorization denied: user {UserId} (role {RoleId}) lacks '{Permission}' on {Path}.",
                _currentUser.UserId, roleId, requirement.Permission,
                _http.HttpContext?.Request.Path.Value);

            try
            {
                await _audit.LogAsync("AUTHZ.DENIED", "Endpoint",
                    Truncate(_http.HttpContext?.Request.Path.Value ?? "unknown", 30),
                    newValue: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        permission = requirement.Permission,
                        userId = _currentUser.UserId
                    }));
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Audit persistence must never break the request pipeline; log-and-continue.
                _logger.LogError(ex, "Failed to persist AUTHZ.DENIED audit row.");
            }

            context.Fail(new AuthorizationFailureReason(this, "Permission denied."));
            return;
        }

        context.Succeed(requirement);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
