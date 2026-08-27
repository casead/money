namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Auth constants from approved specs. Single source for lockout policy.
/// </summary>
public static class AuthRules
{
    /// <summary>SEC-006 / ARCH-006 §13: 5 consecutive failures trigger lock.</summary>
    public const short MaxFailedLogins = 5;

    /// <summary>ARCH-006 §13, API-007 AUTH-001, BLUE-010: 15 minutes (SRS S-A01 said 5 min — superseded, flagged for review).</summary>
    public const int LockoutMinutes = 15;
}
