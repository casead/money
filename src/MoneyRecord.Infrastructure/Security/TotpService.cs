using System.Security.Cryptography;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Infrastructure.Security;

/// <summary>
/// RFC 6238 TOTP (SHA-1, 30-second step, 6 digits) — Google Authenticator compatible.
/// No external dependency: HMAC + Base32 implemented with BCL primitives.
/// </summary>
public sealed class TotpService : ITotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int SecretBytes = 20; // 160-bit secret (RFC 4226 recommendation)
    private const int StepSeconds = 30;
    private const int CodeDigits = 6;
    private const long EpochTicks = 621355968000000000L; // 1970-01-01T00:00:00Z

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        return ToBase32(bytes);
    }

    public bool Validate(string base32Secret, string code, int window = 1)
        => Validate(base32Secret, code, CurrentUnixTime(), window);

    /// <summary>Time-injectable validation (used by unit tests with RFC 6238 vectors).</summary>
    public bool Validate(string base32Secret, string code, long unixTimeSeconds, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) ||
            string.IsNullOrWhiteSpace(code) ||
            code.Length != CodeDigits || !code.All(char.IsDigit))
            return false;

        if (!TryFromBase32(base32Secret, out var secret))
            return false;

        var currentStep = unixTimeSeconds / StepSeconds;

        for (var offset = -window; offset <= window; offset++)
        {
            var candidate = ComputeCode(secret, currentStep + offset);
            // Constant-time comparison
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(candidate),
                    System.Text.Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    public string BuildOtpAuthUri(string base32Secret, string accountName, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var parameters = new Dictionary<string, string>
        {
            ["secret"] = base32Secret,
            ["issuer"] = Uri.EscapeDataString(issuer),
            ["algorithm"] = "SHA1",
            ["digits"] = CodeDigits.ToString(),
            ["period"] = StepSeconds.ToString()
        };
        var query = string.Join("&", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"otpauth://totp/{label}?{query}";
    }

    // ---- internals ----

    private static long CurrentUnixTime() =>
        (DateTime.UtcNow.Ticks - EpochTicks) / TimeSpan.TicksPerSecond;

    /// <summary>RFC 4226 HOTP with SHA-1, dynamic truncation, <see cref="CodeDigits"/> digits.</summary>
    private static string ComputeCode(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.TryHashData(key, counter, hash, out _);

        // Dynamic truncation
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];

        var modulus = 1;
        for (var i = 0; i < CodeDigits; i++) modulus *= 10;

        return (binary % modulus).ToString().PadLeft(CodeDigits, '0');
    }

    private static string ToBase32(byte[] data)
    {
        var sb = new System.Text.StringBuilder(data.Length * 8 / 5 + 1);
        var bitBuffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(bitBuffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Base32Alphabet[(bitBuffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    private static bool TryFromBase32(string value, out byte[] result)
    {
        result = Array.Empty<byte>();
        var clean = new string(value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());
        if (clean.Length == 0 || clean.Any(c => !Base32Alphabet.Contains(c)))
            return false;

        var bitBuffer = 0;
        var bits = 0;
        var output = new List<byte>(clean.Length * 5 / 8);
        foreach (var c in clean)
        {
            bitBuffer = (bitBuffer << 5) | Base32Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((bitBuffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        result = output.ToArray();
        return result.Length > 0;
    }
}
