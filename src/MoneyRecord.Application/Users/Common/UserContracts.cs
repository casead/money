namespace MoneyRecord.Application.Users.Common;

/// <summary>USR-003/004 full profile payload (API-007 §3).</summary>
public sealed record UserDetailResponse(
    long Id,
    string Username,
    string FullName,
    string? Phone,
    int RoleId,
    string RoleCode,
    bool IsActive,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

/// <summary>USR-005 status payload.</summary>
public sealed record UserStatusResponse(long Id, bool IsActive);
