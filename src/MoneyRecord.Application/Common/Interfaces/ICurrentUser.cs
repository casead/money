namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Current authenticated user context, populated from JWT claims by the API layer.
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }

    string? UserName { get; }

    /// <summary>Role id from JWT claim: 1=SuperAdmin, 2=ShopAdmin, 3=Staff (M11).</summary>
    int? RoleId { get; }

    /// <summary>Tenant id from JWT claim — null for platform (SuperAdmin) accounts.</summary>
    long? ShopId { get; }
}

public static class Roles
{
    public const int SuperAdmin = 1;
    public const int ShopAdmin = 2;
    public const int Staff = 3;

    /// <summary>Legacy alias — old code/tests referencing Admin now mean ShopAdmin.</summary>
    public const int Admin = ShopAdmin;

    public static string Code(int roleId) => roleId switch
    {
        SuperAdmin => "SuperAdmin",
        ShopAdmin => "Admin",
        Staff => "Staff",
        _ => $"Role{roleId}"
    };
}
