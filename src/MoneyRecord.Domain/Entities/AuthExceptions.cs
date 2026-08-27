namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;

/// <summary>423 LOCKED_OUT — account temporarily locked after repeated failures (SEC-006).</summary>
public sealed class AccountLockedException : DomainException
{
    public DateTime LockedUntilUtc { get; }

    public AccountLockedException(DateTime lockedUntilUtc)
        : base("LOCKED_OUT", $"Account is locked until {lockedUntilUtc:O}.")
    {
        LockedUntilUtc = lockedUntilUtc;
    }
}

/// <summary>Theft signal — reuse of an already-rotated refresh token (AUTH-003).</summary>
public sealed class RefreshTokenReuseException : BusinessRuleException
{
    public RefreshTokenReuseException()
        : base(ErrorCodes.ConflictState,
            "Refresh token reuse detected. All sessions have been revoked.")
    {
    }
}
