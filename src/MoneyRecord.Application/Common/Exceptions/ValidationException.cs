using FluentValidation.Results;

namespace MoneyRecord.Application.Common.Exceptions;

/// <summary>
/// Aggregated validation failure thrown by ValidationBehavior; mapped to 400 VALIDATION_FAILED.
/// </summary>
public sealed class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation errors occurred.")
    {
        Errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
    }
}
