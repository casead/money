using MoneyRecord.Domain.Common.Errors;
using FluentValidation.Results;

namespace MoneyRecord.Application.Common.Models;

/// <summary>
/// Functional-style result carrying a stable errorCode on failure.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public IDictionary<string, string[]>? ValidationErrors { get; }

    /// <summary>Extra problem-details extensions (e.g. existingCustomerId on 409 DUPLICATE).</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    protected Result(bool isSuccess, string? errorCode = null, string? errorMessage = null,
        IDictionary<string, string[]>? validationErrors = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ValidationErrors = validationErrors;
        Extensions = extensions;
    }

    public static Result Success() => new(true);
    public static Result Failure(string errorCode, string message) => new(false, errorCode, message);

    /// <summary>Failure carrying extra payload surfaced in RFC7807 extensions (API-007 §1.1).</summary>
    public static Result FailureWith(string errorCode, string message,
        IReadOnlyDictionary<string, object?> extensions) =>
        new(false, errorCode, message, extensions: extensions);

    public static Result ValidationFailure(ValidationResult validationResult)
        => new(false, ErrorCodes.ValidationFailed, "Validation failed.",
            validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value) : base(true) => _value = value;

    private Result(string errorCode, string message,
        IDictionary<string, string[]>? validationErrors = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(false, errorCode, message, validationErrors, extensions) => _value = default;

    private Result(TValue value, IReadOnlyDictionary<string, object?> extensions)
        : base(true, extensions: extensions) => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed result.");

    public static Result<TValue> Success(TValue value) => new(value);
    public static new Result<TValue> Failure(string errorCode, string message) => new(errorCode, message);

    public static new Result<TValue> FailureWith(string errorCode, string message,
        IReadOnlyDictionary<string, object?> extensions) =>
        new(errorCode, message, extensions: extensions);
}
