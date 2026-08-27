using System.Security.Claims;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.API.Services;

/// <summary>Reads the authenticated principal populated by JWT bearer middleware.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public long? UserId
    {
        get
        {
            var value = Principal?.FindFirst("sub")?.Value
                        ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName =>
        Principal?.FindFirst("unique_name")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value;

    public int? RoleId
    {
        get
        {
            var value = Principal?.FindFirst("roleId")?.Value;
            return int.TryParse(value, out var role) ? role : null;
        }
    }

    public long? ShopId
    {
        get
        {
            var value = Principal?.FindFirst("shopid")?.Value;
            return long.TryParse(value, out var shopId) ? shopId : null;
        }
    }
}

/// <summary>Per-request client metadata used for audit rows and refresh-token device binding.</summary>
public sealed class RequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _accessor;

    public RequestContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private HttpContext? Http => _accessor.HttpContext;

    public string? IpAddress =>
        Http?.Connection.RemoteIpAddress?.ToString();

    /// <summary>Client-supplied X-Device-Id header; falls back to User-Agent.</summary>
    public string? DeviceInfo =>
        Http?.Request.Headers["X-Device-Id"].ToString() is { Length: > 0 } deviceId
            ? deviceId
            : Http?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}
