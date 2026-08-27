using FluentAssertions;
using MoneyRecord.Domain.Common;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// Golden tests for MMK money math (BRL-004 §2 — integer whole kyats, no negatives).
/// </summary>
public class MoneyTests
{
    [Fact]
    public void Constructor_WithNegativeKyats_Throws()
    {
        var act = () => new Money(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_AtMaxLimit_Succeeds()
    {
        var money = new Money(Money.MaxKyats);
        money.Kyats.Should().Be(Money.MaxKyats);
    }

    [Fact]
    public void Constructor_AboveMaxLimit_Throws()
    {
        var act = () => new Money(Money.MaxKyats + 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Addition_WithinLimits_SumsCorrectly()
    {
        var left = new Money(100_000);
        var right = new Money(50_000);

        (left + right).Kyats.Should().Be(150_000);
    }

    [Fact]
    public void Addition_Overflow_Throws()
    {
        var left = new Money(Money.MaxKyats);
        var right = new Money(1);

        // BRL-004 §2: amounts are hard-capped at MaxKyats by the constructor guard.
        var act = () => left + right;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SubtractGuarded_WhenSufficient_Subtracts()
    {
        var balance = new Money(100_000);

        Money.SubtractGuarded(balance, new Money(30_000)).Kyats.Should().Be(70_000);
    }

    [Fact]
    public void SubtractGuarded_WhenInsufficient_ThrowsInsufficient()
    {
        var balance = new Money(100_000);

        var act = () => Money.SubtractGuarded(balance, new Money(100_001));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_SameKyats_AreEqual()
    {
        (new Money(5_000) == new Money(5_000)).Should().BeTrue();
        (new Money(5_000) == new Money(6_000)).Should().BeFalse();
    }

    [Theory]
    [InlineData(1_234_567, "1,234,567 Ks")]
    public void ToString_FormatsWithGrouping(long kyats, string expected)
    {
        new Money(kyats).ToString().Should().Be(expected);
    }
}
