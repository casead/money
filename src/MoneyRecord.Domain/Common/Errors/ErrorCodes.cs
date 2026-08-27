namespace MoneyRecord.Domain.Common.Errors;

/// <summary>
/// Stable error codes surfaced to clients via the standard error envelope (API-007 §1.2).
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string ConflictState = "CONFLICT_STATE";
    public const string InsufficientCash = "TXN_INSUFFICIENT_CASH";
    public const string InsufficientFloat = "TXN_INSUFFICIENT_FLOAT";
    public const string DuplicateRequest = "DUPLICATE_REQUEST";
    public const string LockTimeout = "LOCK_TIMEOUT";
    public const string InvalidOperation = "INVALID_OPERATION";

    // ---- User management (API-007 USR-*) ----
    public const string Duplicate = "DUPLICATE";
    public const string SelfRoleChange = "SELF_ROLE_CHANGE";
    public const string SelfDeactivate = "SELF_DEACTIVATE";
    public const string LastAdmin = "LAST_ADMIN";

    // ---- Auth / MFA (SEC-00x hardening) ----
    public const string MfaRequired = "MFA_REQUIRED";

    // ---- Balances (API-007 BAL-003 / BR-034) ----
    public const string InsufficientForDecrease = "INSUFFICIENT_FOR_DECREASE";
}
