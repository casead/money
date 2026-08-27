using FluentValidation.TestHelper;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Domain.Entities;
using FluentAssertions;

namespace MoneyRecord.UnitTests.Application;

/// <summary>
/// Module 5 unit coverage: BAL-003 validation rules, BR-034 integrity flag math,
/// and account masking used across balance payloads.
/// </summary>
public class BalanceModuleTests
{
    private readonly AdjustBalanceCommandValidator _adjust = new();
    private readonly CreateWalletAccountCommandValidator _createAccount = new();

    // ---- BAL-003 validation ----

    [Fact]
    public void ValidCashAdjustment_Passes()
    {
        var result = _adjust.TestValidate(new AdjustBalanceCommand(
            "cash", null, "INCREASE", 50000, "Count difference from day-close", null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ShortReason_Fails_Br020()
    {
        var result = _adjust.TestValidate(new AdjustBalanceCommand(
            "cash", null, "INCREASE", 5000, "too short", null));
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ZeroAmount_Fails()
    {
        var result = _adjust.TestValidate(new AdjustBalanceCommand(
            "wallet", 1, "DECREASE", 0, "reason long enough here", null));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void WalletScope_WithoutAccountId_Fails()
    {
        var result = _adjust.TestValidate(new AdjustBalanceCommand(
            "wallet", null, "DECREASE", 5000, "reason long enough here", null));
        result.ShouldHaveValidationErrorFor(x => x.WalletAccountId);
    }

    [Fact]
    public void BadDirection_Fails()
    {
        var result = _adjust.TestValidate(new AdjustBalanceCommand(
            "cash", null, "SIDEWAYS", 5000, "reason long enough here", null));
        result.ShouldHaveValidationErrorFor(x => x.Direction);
    }

    // ---- Account creation ----

    [Fact]
    public void NegativeOpeningFloat_Fails()
    {
        var result = _createAccount.TestValidate(
            new CreateWalletAccountCommand(1, "Wave Main", "09770001111", -100));
        result.ShouldHaveValidationErrorFor(x => x.OpeningFloat);
    }

    [Fact]
    public void ValidAccount_Passes()
    {
        var result = _createAccount.TestValidate(
            new CreateWalletAccountCommand(2, "KBZ Main", "09887766554", 250000));
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---- Integrity math (DR-08) ----

    [Fact]
    public void IntegrityFlag_Null_WhenCacheMatchesLedgerSum()
    {
        IntegrityCheck.Flag(150000, IntegrityCheck.SignedSum(200000, 50000))
            .Should().BeNull();
    }

    [Fact]
    public void IntegrityFlag_Mismatch_OnDrift()
    {
        IntegrityCheck.Flag(140000, IntegrityCheck.SignedSum(200000, 50000))
            .Should().Be("MISMATCH");
    }

    [Fact]
    public void Masking_ShowsLast4()
    {
        CreateWalletAccountCommandHandler.Mask("09770001111").Should().Be("•••1111");
        CreateWalletAccountCommandHandler.Mask("123").Should().Be("123");
        CreateWalletAccountCommandHandler.Mask(null).Should().BeNull();
    }
}
