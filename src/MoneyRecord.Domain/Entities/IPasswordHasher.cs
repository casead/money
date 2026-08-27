namespace MoneyRecord.Domain.Entities;

/// <summary>Abstraction over password hashing. Implemented in Infrastructure (PBKDF2).</summary>
public interface IPasswordHasher
{
    /// <summary>Returns modular-crypt-format hash string.</summary>
    string Hash(string password);

    /// <summary>Constant-time verification against a stored hash.</summary>
    bool Verify(string password, string storedHash);
}
