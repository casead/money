namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Role ↔ Permission M:N junction (DBD-005 T04). Composite PK (RoleId, PermissionId).
/// Seed data must mirror <see cref="Common.Rbac.RolePermissionRegistry"/> (unit-test asserted).
/// </summary>
public class RolePermission
{
    public int RoleId { get; private set; }

    public int PermissionId { get; private set; }

    private RolePermission() { } // EF Core

    public RolePermission(int roleId, int permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
