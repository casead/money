namespace MoneyRecord.Domain.Common;

/// <summary>
/// Base entity with identity only. Audit fields are added on concrete entities.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}
