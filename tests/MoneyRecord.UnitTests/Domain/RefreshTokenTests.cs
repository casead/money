using FluentAssertions;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;
using MoneyRecord.UnitTests.Common;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// Refresh token lifecycle (AUTH-003 / TC-300d): issue → rotate → reuse-detect.
/// </summary>
public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken IssueToken(string hash = "hash-1") =>
        RefreshToken.Issue(userId: 42, tokenHash: hash, nowUtc: Now,
            lifetimeDays: 7, deviceInfo: "Pixel 8", ipAddress: "10.0.0.1");

    [Fact]
    public void Issue_ExpiryIsNowPlusLifetime()
    {
        var rt = IssueToken();

        rt.CreatedAtUtc.Should().Be(Now);
        rt.ExpiresAtUtc.Should().Be(Now.AddDays(7));
        rt.RevokedAtUtc.Should().BeNull();
        rt.DeviceInfo.Should().Be("Pixel 8");
    }

    [Fact]
    public void Issue_DeviceInfoLongerThan200_Truncated()
    {
        var rt = RefreshToken.Issue(42, "h", Now, 7, new string('x', 500), "10.0.0.1");

        rt.DeviceInfo.Should().HaveLength(200);
    }

    [Fact]
    public void Rotate_MarksRevokedAndLinksSuccessor()
    {
        var old = IssueToken();
        var successorHash = ITokenService.HashRefreshToken("raw-new-token");

        old.Rotate(successorHash, Now.AddMinutes(15));

        old.RevokedAtUtc.Should().Be(Now.AddMinutes(15));
        old.ReplacedByTokenHash.Should().Be(successorHash);
    }

    [Fact]
    public void Revoke_WithoutSuccessor_IsTheftSignalState()
    {
        var stolen = IssueToken();

        stolen.Revoke(Now.AddMinutes(20));

        stolen.RevokedAtUtc.Should().NotBeNull();
        stolen.ReplacedByTokenHash.Should().BeNull(); // revoked with no replacement = reuse detected
    }
}
