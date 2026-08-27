using FluentAssertions;
using FluentValidation.Results;

namespace MoneyRecord.UnitTests.Application;

/// <summary>
/// M1 acceptance: validation failure produces structured errors for the 400 envelope.
/// </summary>
public class ValidationExceptionTests
{
    [Fact]
    public void Groups_Failures_By_PropertyName()
    {
        var failures = new[]
        {
            new ValidationFailure("Amount", "ပမာဏ ၀ ထက် ကြီးရမည်"),
            new ValidationFailure("Amount", "ပမာဏ အလွန်ကြီးသည်"),
            new ValidationFailure("Phone", "ဖုန်းနံပါတ် format မမှန်ကန်ပါ")
        };

        var exception = new MoneyRecord.Application.Common.Exceptions.ValidationException(failures);

        exception.Errors.Should().HaveCount(2);
        exception.Errors["Amount"].Should().HaveCount(2);
        exception.Errors["Phone"].Should().HaveCount(1);
    }
}
