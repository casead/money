using FluentAssertions;
using MoneyRecord.Domain.Common;
using MoneyRecord.Infrastructure.Security;

namespace MoneyRecord.UnitTests.Infrastructure;

/// <summary>
/// PBKDF2-SHA512 hasher (TC-300e): salted, iterated, constant-time verify, plaintext never stored.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var a = _hasher.Hash("Admin@12345");
        var b = _hasher.Hash("Admin@12345");

        a.Should().NotBe(b); // unique random salt
    }

    [Fact]
    public void Hash_OutputFormat_IsPbkdf2ModularCrypt()
    {
        var hash = _hasher.Hash("Secret#2026");

        hash.Should().StartWith("pbkdf2-sha512$100000$");
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("Secret#2026");

        _hasher.Verify("Secret#2026", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("Secret#2026");

        _hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedStoredHash_ReturnsFalseWithoutThrowing()
    {
        _hasher.Verify("whatever", "not-a-valid-hash").Should().BeFalse();
    }

    [Fact]
    public void Hash_DoesNotContainPlaintext()
    {
        const string password = "MyVisiblePass99";

        var hash = _hasher.Hash(password);

        hash.Should().NotContain(password);
    }
}
