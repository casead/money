namespace MoneyRecord.Domain.Common;

/// <summary>
/// MMK money value object — whole kyats only (BRL-004 §2: no subunits).
/// Overflow-safe checked arithmetic; negative values are invalid by construction.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public const long MaxKyats = 9_999_999_999_999; // 13 digits — well below long.MaxValue

    public long Kyats { get; }

    public Money(long kyats)
    {
        if (kyats < 0)
            throw new ArgumentOutOfRangeException(nameof(kyats), "Amount cannot be negative.");
        if (kyats > MaxKyats)
            throw new ArgumentOutOfRangeException(nameof(kyats), "Amount exceeds maximum allowed.");
        Kyats = kyats;
    }

    public static Money Zero => new(0);

    /// <summary>Signed difference used by ledger math; may be negative.</summary>
    public static long Difference(Money left, Money right) => left.Kyats - right.Kyats;

    public static Money operator +(Money left, Money right) => new(checked(left.Kyats + right.Kyats));

    /// <summary>Subtraction that never yields a negative balance.</summary>
    public static Money SubtractGuarded(Money left, Money right)
    {
        if (right.Kyats > left.Kyats)
            throw new InvalidOperationException("Insufficient amount for subtraction.");
        return new(left.Kyats - right.Kyats);
    }

    public bool Equals(Money other) => Kyats == other.Kyats;
    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => Kyats.GetHashCode();
    public int CompareTo(Money other) => Kyats.CompareTo(other.Kyats);

    public static bool operator ==(Money left, Money right) => left.Equals(right);
    public static bool operator !=(Money left, Money right) => !left.Equals(right);
    public static bool operator >(Money left, Money right) => left.Kyats > right.Kyats;
    public static bool operator <(Money left, Money right) => left.Kyats < right.Kyats;
    public static bool operator >=(Money left, Money right) => left.Kyats >= right.Kyats;
    public static bool operator <=(Money left, Money right) => left.Kyats <= right.Kyats;

    public override string ToString() => $"{Kyats:N0} Ks";
}
