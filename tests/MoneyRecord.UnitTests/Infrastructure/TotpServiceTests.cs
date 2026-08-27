using FluentAssertions;
using MoneyRecord.Infrastructure.Security;

namespace MoneyRecord.UnitTests.Infrastructure;

/// <summary>
/// RFC 6238 conformance: secret "12345678901234567890" (ASCII) → Base32
/// GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ; 6-digit codes are the last 6 of the RFC's 8-digit vectors.
/// </summary>
public class TotpServiceTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private readonly TotpService _totp = new();

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Rfc6238_Vectors_AreAccepted(long unixTime, string code)
    {
        _totp.Validate(Secret, code, unixTime).Should().BeTrue();
    }

    [Fact]
    public void WrongCode_IsRejected()
    {
        _totp.Validate(Secret, "000000", 59L).Should().BeFalse();
        _totp.Validate(Secret, "28708", 59L).Should().BeFalse();   // wrong length
        _totp.Validate(Secret, "28708a", 59L).Should().BeFalse();  // non-digit
        _totp.Validate("", "287082", 59L).Should().BeFalse();      // empty secret
    }

    [Fact]
    public void AdjacentStep_WithinWindow_IsAccepted()
    {
        // Code for step N accepted at step N+1 (30s clock drift)
        var nextStepTime = (59L / 30 + 1) * 30;
        _totp.Validate(Secret, "287082", nextStepTime).Should().BeTrue();
    }

    [Fact]
    public void DistantStep_BeyondWindow_IsRejected()
    {
        var farFuture = 59L + 30 * 5; // 5 steps later
        _totp.Validate(Secret, "287082", farFuture).Should().BeFalse();
    }

    [Fact]
    public void GeneratedSecret_RoundTrips()
    {
        var secret = _totp.GenerateSecret();
        secret.Should().HaveLength(32); // 20 bytes → 32 Base32 chars

        var uri = _totp.BuildOtpAuthUri(secret, "admin", "MoneyRecord");
        uri.Should().StartWith("otpauth://totp/MoneyRecord%3Aadmin?");
        uri.Should().Contain($"secret={secret}");
        uri.Should().Contain("digits=6").And.Contain("period=30");
    }
}
