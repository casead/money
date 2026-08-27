namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>RFC 6238 TOTP abstraction (implemented in Infrastructure — HMAC-SHA1, 6 digits, 30s).</summary>
public interface ITotpService
{
    /// <summary>Generates a random 20-byte secret, Base32-encoded (no padding).</summary>
    string GenerateSecret();

    /// <summary>
    /// Validates a 6-digit code against the Base32 secret. Allows ±<paramref name="window"/>
    /// time steps for clock drift.
    /// </summary>
    bool Validate(string base32Secret, string code, int window = 1);

    /// <summary>otpauth:// URI for QR rendering (Google Authenticator compatible).</summary>
    string BuildOtpAuthUri(string base32Secret, string accountName, string issuer);
}
