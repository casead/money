namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Granular permission catalog (DBD-005 T03). v1 enforcement is code-level via
/// <see cref="Common.Rbac.RolePermissionRegistry"/>; this table enables runtime-configurable
/// RBAC later without a schema migration. Seeded with fixed ids.
/// </summary>
public class Permission
{
    public int Id { get; private set; }

    /// <summary>Stable code, e.g. 'user.manage' (API-007 §13.2).</summary>
    public string Code { get; private set; } = default!;

    /// <summary>One of DBD module buckets: Auth/Customer/Txn/Balance/Fee/Report/Admin.</summary>
    public string Module { get; private set; } = default!;

    public string? Description { get; private set; }

    private Permission() { } // EF Core

    public Permission(int id, string code, string module, string? description)
    {
        Id = id;
        Code = code;
        Module = module;
        Description = description;
    }
}
