using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Domain.Common.Exceptions;

/// <summary>
/// Base exception carrying a stable errorCode; mapped to ProblemDetails by middleware.
/// </summary>
public abstract class DomainException : Exception
{
    public string ErrorCode { get; }

    protected DomainException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}

public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string errorCode, string message) : base(errorCode, message) { }
}

public sealed class InsufficientFloatException : BusinessRuleException
{
    public InsufficientFloatException(long currentBalance)
        : base(ErrorCodes.InsufficientFloat, $"Wallet float မလုံလောက်ပါ (Current: {currentBalance:N0} Ks)")
        => CurrentBalance = currentBalance;

    public long CurrentBalance { get; }
}

public sealed class InsufficientCashException : BusinessRuleException
{
    public InsufficientCashException(long currentBalance)
        : base(ErrorCodes.InsufficientCash, $"လက်ကျန်ငွေသား မလုံလောက်ပါ (Current: {currentBalance:N0} Ks)")
        => CurrentBalance = currentBalance;

    public long CurrentBalance { get; }
}

public sealed class ConflictStateException : BusinessRuleException
{
    public ConflictStateException(string message) : base(ErrorCodes.ConflictState, message) { }
}

/// <summary>UPDLOCK wait exceeded the 5s budget (BR-035) — 409, retry advised.</summary>
public sealed class LockTimeoutException : DomainException
{
    public LockTimeoutException()
        : base(ErrorCodes.LockTimeout,
            "Balance lock အချိန်ကုန်သွားပါသည် — ခဏနေပြီး ပြန်ကြိုးစားပါ။") { }
}

/// <summary>Same Idempotency-Key with different payload (API-007 §1.4 #4) — 409.</summary>
public sealed class DuplicateRequestException : DomainException
{
    public DuplicateRequestException()
        : base(ErrorCodes.DuplicateRequest,
            "Idempotency-Key တူညီသော်လည်း payload ကွာခြားနေပါသည်။") { }
}

/// <summary>BR-034 hard floor: DECREASE would push a balance below zero.</summary>
public sealed class InsufficientForDecreaseException : BusinessRuleException
{
    public InsufficientForDecreaseException(long currentBalance)
        : base(ErrorCodes.InsufficientForDecrease,
            $"လက်ရှိ balance ({currentBalance:N0} Ks) ထက် များသော ပမာဏ ဖြတ်လို့ မရပါ။")
        => CurrentBalance = currentBalance;

    public long CurrentBalance { get; }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object key)
        : base(ErrorCodes.NotFound, $"{entityName} '{key}' ကို ရှာမတွေ့ပါ။") { }
}
