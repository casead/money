using FluentValidation.TestHelper;
using MoneyRecord.Application.Users.Commands;

namespace MoneyRecord.UnitTests.Application;

/// <summary>
/// USR-002/004 field rules: S-A02 password policy, username format (FR-006), role range.
/// </summary>
public class UserCommandValidatorTests
{
    private static CreateUserCommand ValidCommand() => new(
        Username: "staff01",
        Password: "Passw0rd!",
        FullName: "Staff One",
        Phone: "0977000111",
        RoleId: 2);

    private readonly CreateUserCommandValidator _create = new();
    private readonly UpdateUserCommandValidator _update = new();

    // ---- happy path ----

    [Fact]
    public void ValidCreateCommand_Passes()
    {
        var result = _create.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---- username ----

    [Theory]
    [InlineData("ab")]                    // too short
    [InlineData("this-username-is-far-too-long-for-the-rule")] // > 50
    [InlineData("has space")]
    [InlineData("myanmar-က")]             // non-ASCII
    public void InvalidUsername_Fails(string username)
    {
        var result = _create.TestValidate(ValidCommand() with { Username = username });
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    // ---- password policy (S-A02) ----

    [Theory]
    [InlineData("Sh0rt")]                 // < 8
    [InlineData("alllettersonly")]        // no digit
    [InlineData("12345678")]              // no letter
    public void WeakPassword_Fails(string password)
    {
        var result = _create.TestValidate(ValidCommand() with { Password = password });
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void EightCharLetterDigitPassword_Passes()
    {
        var result = _create.TestValidate(ValidCommand() with { Password = "abc12345" });
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    // ---- fullName / phone / role ----

    [Fact]
    public void ShortFullName_Fails()
    {
        var result = _create.TestValidate(ValidCommand() with { FullName = "A" });
        result.ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Theory]
    [InlineData("09-77-000")]             // non-digit
    [InlineData("0977")]                  // too short
    public void InvalidPhone_Fails(string phone)
    {
        var result = _create.TestValidate(ValidCommand() with { Phone = phone });
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)] // SuperAdmin is platform-level — never assignable via USR-002
    [InlineData(99)]
    public void UnknownRoleId_Fails(int roleId)
    {
        var result = _create.TestValidate(ValidCommand() with { RoleId = roleId });
        result.ShouldHaveValidationErrorFor(x => x.RoleId);
    }

    [Fact]
    public void UpdateValidator_Accepts_PartialPayloads()
    {
        var result = _update.TestValidate(new UpdateUserCommand(5, null, null, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateValidator_Rejects_UnknownRole()
    {
        var result = _update.TestValidate(new UpdateUserCommand(5, null, null, 7));
        result.ShouldHaveValidationErrorFor(x => x.RoleId);
    }
}
