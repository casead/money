using FluentAssertions;
using FluentValidation.TestHelper;
using MoneyRecord.Application.Customers.Commands;
using MoneyRecord.Application.Customers.Queries;

namespace MoneyRecord.UnitTests.Application;

/// <summary>CUS-002/004 field rules (FR-010): name length, Myanmar phone format, limits.</summary>
public class CustomerCommandValidatorTests
{
    private readonly CreateCustomerCommandValidator _create = new();
    private readonly UpdateCustomerCommandValidator _update = new();

    private static CreateCustomerCommand Valid() =>
        new("Aung Aung", "0977000111", null, null);

    [Fact]
    public void ValidCreate_Passes()
    {
        _create.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("+95977000111")]
    [InlineData("09 770 001 11")]
    public void Phone_Variants_NormalizeAndPass(string phone)
    {
        var result = _create.TestValidate(Valid() with { Phone = phone });
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void ShortName_Fails()
    {
        var result = _create.TestValidate(Valid() with { FullName = "A" });
        result.ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Fact]
    public void InvalidPhone_Fails()
    {
        var result = _create.TestValidate(Valid() with { Phone = "12345" });
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void LongAddress_Fails()
    {
        var result = _create.TestValidate(Valid() with { Address = new string('x', 201) });
        result.ShouldHaveValidationErrorFor(x => x.Address);
    }

    [Fact]
    public void LongNote_Fails()
    {
        var result = _create.TestValidate(Valid() with { Note = new string('x', 501) });
        result.ShouldHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void Update_PartialPayloads_Pass()
    {
        var result = _update.TestValidate(new UpdateCustomerCommand(5, null, null, null, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_BadPhone_Fails()
    {
        var result = _update.TestValidate(
            new UpdateCustomerCommand(5, null, "99999", null, null));
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }
}
