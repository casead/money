namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Append-only audit trail (DBD-005 T22). INSERT-only — no UPDATE/DELETE ever.
/// </summary>
public class AuditLog
{
    public long Id { get; private set; }

    /// <summary>e.g. 'AUTH.LOGIN', 'AUTH.REFRESH_REUSE_DETECTED', 'AUTH.LOGOUT'.</summary>
    public string ActionCode { get; private set; } = default!;

    public string EntityType { get; private set; } = default!;

    /// <summary>String to support composite/any keys.</summary>
    public string EntityId { get; private set; } = default!;

    public string? OldValuesJson { get; private set; }

    public string? NewValuesJson { get; private set; }

    public string? IpAddress { get; private set; }

    public string? DeviceInfo { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>NULL = system action.</summary>
    public long? ActorUserId { get; private set; }

    /// <summary>Tenancy (M11): null = platform-level action.</summary>
    public long? ShopId { get; private set; }

    private AuditLog() { } // EF Core

    public static AuditLog Create(string actionCode, string entityType, string entityId,
        string? oldValuesJson, string? newValuesJson,
        string? ipAddress, string? deviceInfo, long? actorUserId, IClock clock,
        long? shopId = null)
    {
        return new AuditLog
        {
            ActionCode = actionCode,
            EntityType = entityType,
            EntityId = entityId,
            OldValuesJson = oldValuesJson,
            NewValuesJson = newValuesJson,
            IpAddress = ipAddress,
            DeviceInfo = deviceInfo,
            ActorUserId = actorUserId,
            ShopId = shopId,
            CreatedAtUtc = clock.UtcNow
        };
    }
}
