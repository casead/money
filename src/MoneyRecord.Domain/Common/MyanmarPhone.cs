namespace MoneyRecord.Domain.Common;

/// <summary>
/// Myanmar mobile phone normalization (FR-010 Step 4 / CUS-005).
/// Canonical storage format: 09XXXXXXXXX (matches ^09\d{7,9}$).
/// Accepted inputs: '09 770 001 111', '09-770-001-11', '+95977000111', '95977000111'.
/// </summary>
public static class MyanmarPhone
{
    /// <summary>Canonical format check (API-007 CUS-002 validation).</summary>
    public const string Pattern = "^09\\d{7,9}$";

    /// <summary>Normalizes common Myanmar input variants; returns null when unparseable.</summary>
    public static string? TryNormalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Strip separators and whitespace.
        var digits = new string(input.Trim()
            .Where(char.IsDigit)
            .ToArray());

        // International forms: +959… / 00959… / 959…
        if (digits.StartsWith("959", StringComparison.Ordinal) && digits.Length >= 10)
            digits = "0" + digits[2..];
        else if (digits.StartsWith("0095", StringComparison.Ordinal))
            digits = "0" + digits[4..];

        return IsCanonical(digits) ? digits : null;
    }

    public static bool IsCanonical(string phone) =>
        !string.IsNullOrEmpty(phone) &&
        System.Text.RegularExpressions.Regex.IsMatch(phone, Pattern);

    /// <summary>Client-parity masking: '0977000111' → '0977•••111'.</summary>
    public static string Mask(string phone)
    {
        if (phone.Length < 7)
            return phone;
        return $"{phone[..4]}•••{phone[^3..]}";
    }
}
