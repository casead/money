using FluentAssertions;
using MoneyRecord.Domain.Common;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// Myanmar phone normalization parity (TC-400 series prerequisite):
/// shared rule between client hint and server storage (FR-010 Step 4).
/// </summary>
public class MyanmarPhoneTests
{
    [Theory]
    [InlineData("0977000111", "0977000111")]
    [InlineData("09 770 001 11", "0977000111")]          // spaces stripped
    [InlineData("09-770-001-11", "0977000111")]          // dashes stripped
    [InlineData("+95977000111", "0977000111")]           // international
    [InlineData("0095977000111", "0977000111")]          // intl with leading zeros
    [InlineData("95977000111", "0977000111")]            // no plus prefix
    public void Normalize_Accepts_CommonVariants(string input, string expected)
    {
        MyanmarPhone.TryNormalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]              // not a mobile pattern
    [InlineData("08977700111")]        // wrong operator prefix
    [InlineData("09770001")]           // too short (8 digits after 09)
    [InlineData("abcdef")]
    public void Normalize_Rejects_InvalidInput(string input)
    {
        MyanmarPhone.TryNormalize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("0977000111", true)]   // 10 digits total (09 + 8)
    [InlineData("09770001111", true)]  // 11 digits total (09 + 9) — max length
    [InlineData("097700011111", false)]// 12 digits — too long
    [InlineData("97700011", false)]
    public void IsCanonical_Validates_CanonicalFormat(string phone, bool expected)
    {
        MyanmarPhone.IsCanonical(phone).Should().Be(expected);
    }

    [Fact]
    public void Mask_Hides_MiddleDigits()
    {
        MyanmarPhone.Mask("0977000111").Should().Be("0977•••111");
        MyanmarPhone.Mask("short").Should().Be("short");
    }
}
