namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Role definitions (DBD-005 T02). Seeded: (1,'Admin'), (2,'Staff'). System roles cannot be deleted.
/// </summary>
public class Role
{
    public int Id { get; private set; }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    /// <summary>1 = system role, cannot be deleted or modified.</summary>
    public bool IsSystemRole { get; private set; }

    private Role() { } // EF Core

    public Role(int id, string code, string name, string? description, bool isSystemRole)
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        IsSystemRole = isSystemRole;
    }
}
