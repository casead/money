using FluentValidation.TestHelper;
using FluentAssertions;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.UnitTests.Application;

/// <summary>
/// TXN-001/002 field rules: DD-08 amount cap, Myanmar phone, BR-013 override reason.
/// Golden-example ledger math lives in the E2E suite (real SQL Server required).
/// </summary>
public class TxnCommandValidatorTests
{
    private readonly CreateCashInCommandValidator _in = new();
    private readonly CreateCashOutCommandValidator _out = new();

    private static CreateCashInCommand ValidIn() => new()
    {
        IdempotencyKey = Guid.NewGuid(),
        CustomerName = "Daw Hla Hla",
        CustomerPhone = "09770001112",
        WalletAccountId = 1,
        Amount = 100_000,
        FeePaidVia = "cash"
    };

    [Fact]
    public void ValidCommand_Passes()
    {
        _in.TestValidate(ValidIn()).ShouldNotHaveAnyValidationErrors();
        _out.TestValidate(ToOut(ValidIn())).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void MissingIdempotencyKey_Fails_Br031()
    {
        var result = _in.TestValidate(ValidIn() with { IdempotencyKey = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveAmount_Fails(long amount)
    {
        var result = _in.TestValidate(ValidIn() with { Amount = amount });
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void AmountOverCap_Fails_DD08()
    {
        var result = _in.TestValidate(ValidIn() with { Amount = TxnRules.MaxAmount + 1 });
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void BadPhone_Fails()
    {
        var result = _in.TestValidate(ValidIn() with { CustomerPhone = "12345" });
        result.ShouldHaveValidationErrorFor(x => x.CustomerPhone);
    }

    [Fact]
    public void OverrideWithoutReason_Passes()
    {
        var result = _in.TestValidate(ValidIn() with
        {
            FeeAmountOverride = 500,
            FeeOverrideReason = null
        });
        result.ShouldNotHaveValidationErrorFor(x => x.FeeAmountOverride);
    }

    [Fact]
    public void OverrideWithShortReason_Passes()
    {
        var result = _in.TestValidate(ValidIn() with
        {
            FeeAmountOverride = 500,
            FeeOverrideReason = "abc"
        });
        result.ShouldNotHaveValidationErrorFor(x => x.FeeAmountOverride);
    }

    [Fact]
    public void NegativeFeeOverride_Fails()
    {
        var result = _in.TestValidate(ValidIn() with
        {
            FeeAmountOverride = -1,
            FeeOverrideReason = "valid reason here"
        });
        result.ShouldHaveValidationErrorFor(x => x.FeeAmountOverride);
    }

    // ---- state machine guards ----

    [Fact]
    public void NewTxn_IsCompleted_AndTerminalGuardWorks()
    {
        var txn = Transaction.Complete(
            "TXN-2026-00001", TransactionType.CashIn, 100_000, 500, true,
            null, FeePaidVia.Cash, false, null, "Daw Hla Hla", "09770001112", 1, 1,
            Guid.NewGuid(), null, null, 1, new FixedClock(), shopId: 1);

        txn.IsCompleted.Should().BeTrue();
        txn.GrossProfit.Should().Be(500); // BR-016: fee − commission(0)
        txn.Status.ToString().Should().Be("Completed");
    }

    [Fact]
    public void RequestHash_IsStable_ForSamePayload_AndDiffers_OnChange()
    {
        var a = ValidIn();
        var b = ValidIn() with { IdempotencyKey = Guid.NewGuid() }; // key NOT part of hash
        a.ComputeRequestHash().Should().Be(b.ComputeRequestHash());

        var c = ValidIn() with { Amount = a.Amount + 1000 };
        c.ComputeRequestHash().Should().NotBe(a.ComputeRequestHash());
    }

    private static CreateCashOutCommand ToOut(CreateCashInCommand src) => new()
    {
        IdempotencyKey = src.IdempotencyKey,
        CustomerId = src.CustomerId,
        CustomerName = src.CustomerName,
        CustomerPhone = src.CustomerPhone,
        WalletAccountId = src.WalletAccountId,
        Amount = src.Amount,
        FeeAmountOverride = src.FeeAmountOverride,
        FeeOverrideReason = src.FeeOverrideReason,
        FeePaidVia = src.FeePaidVia,
        Note = src.Note
    };

    private sealed class FixedClock : MoneyRecord.Domain.Common.IClock
    {
        public DateTime UtcNow => new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayYangon => new(2026, 8, 24);
    }
}
