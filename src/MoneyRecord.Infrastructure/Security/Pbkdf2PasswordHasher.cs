using System.Security.Cryptography;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA512 password hasher, ≥100k iterations with per-user random salt (ARCH-006 §13, BLUE-010).
/// Format: pbkdf2-sha512$iter$saltB64$hashB64
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 32;          // 256-bit salt
    private const int HashSize = 64;          // 512-bit derived key
    private const int Iterations = 100_000;   // BLUE-010 readiness: ≥100k

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return $"pbkdf2-sha512${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        // Parse "$"-delimited format; constant-time compare on the derived bytes.
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha512")
            return false;

        if (!int.TryParse(parts[1], out int iterations) || iterations < 1)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
