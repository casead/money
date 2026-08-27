namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// System login account (DBD-005 T01). Soft-delete master data — never hard deleted.
/// </summary>
public class User
{
    public long Id { get; private set; }

    public string Username { get; private set; } = default!;

    /// <summary>PBKDF2 hash in modular crypt format. Plaintext never stored or logged (TC-300e).</summary>
    public string PasswordHash { get; private set; } = default!;

    public string FullName { get; private set; } = default!;

    public string? Phone { get; private set; }

    public int RoleId { get; private set; }

    public Role Role { get; private set; } = default!;

    /// <summary>Tenancy (M11): null = platform account (SuperAdmin), else shop member.</summary>
    public long? ShopId { get; private set; }

    public Shop? Shop { get; private set; }

    /// <summary>Deactivate instead of delete (BC-02 soft-lock).</summary>
    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    // ---- Lockout state (SEC-006) ----

    public short FailedLoginCount { get; private set; }

    /// <summary>UTC instant until which login is locked; null when not locked.</summary>
    public DateTime? LockedUntilUtc { get; private set; }

    // ---- TOTP MFA state (hardening) ----

    /// <summary>Active TOTP second factor. Login requires a valid 6-digit code when true.</summary>
    public bool MfaEnabled { get; private set; }

    /// <summary>Base32 secret used while <see cref="MfaEnabled"/> is true.</summary>
    public string? MfaSecret { get; private set; }

    /// <summary>Secret awaiting first successful code confirmation (enrollment in progress).</summary>
    public string? MfaPendingSecret { get; private set; }

    // ---- Audit trail ----

    public DateTime CreatedAtUtc { get; private set; }

    public long? CreatedByUserId { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public long? ModifiedByUserId { get; private set; }

    private User() { } // EF Core

    public static User Create(string username, string passwordHash, string fullName,
        int roleId, long actorUserId, IClock clock, long? shopId = null)
    {
        return new User
        {
            Username = username.Trim(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            RoleId = roleId,
            ShopId = shopId,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
    }

    /// <summary>
    /// Verifies a candidate password and updates lockout counters.
    /// Returns false on bad credentials (increments counter).
    /// Throws <see cref="AccountLockedException"/> while locked (423 LOCKED_OUT).
    /// </summary>
    public bool VerifyLogin(string candidatePassword, IPasswordHasher hasher, IClock clock)
    {
        if (IsDeleted || !IsActive)
            return false;

        if (LockedUntilUtc is { } until && until > clock.UtcNow)
            throw new AccountLockedException(until);

        if (hasher.Verify(candidatePassword, PasswordHash))
        {
            FailedLoginCount = 0;
            LockedUntilUtc = null;
            LastLoginAtUtc = clock.UtcNow;
            return true;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= AuthRules.MaxFailedLogins)
        {
            FailedLoginCount = 0;
            LockedUntilUtc = clock.UtcNow.AddMinutes(AuthRules.LockoutMinutes);
        }
        return false;
    }

    /// <summary>Soft-lock (BC-02). Callers must also revoke all refresh tokens.</summary>
    public void Deactivate(long actorUserId, IClock clock)
    {
        IsActive = false;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    public void Reactivate(long actorUserId, IClock clock)
    {
        IsActive = true;
        FailedLoginCount = 0;
        LockedUntilUtc = null;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    public void UpdateProfile(string? fullName, string? phone, long actorUserId, IClock clock)
    {
        if (fullName is not null) FullName = fullName.Trim();
        if (phone is not null) Phone = phone;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>Role reassignment (USR-004). Callers must run UserManagementRules guards first.</summary>
    public void ChangeRole(int newRoleId, long actorUserId, IClock clock)
    {
        if (newRoleId == RoleId)
            return;

        RoleId = newRoleId;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>Sets a new password hash and clears lockout state.</summary>
    public void SetPassword(string newPasswordHash, long actorUserId, IClock clock)
    {
        PasswordHash = newPasswordHash;
        FailedLoginCount = 0;
        LockedUntilUtc = null;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    // ---- TOTP MFA (hardening) ----

    /// <summary>Stores the enrollment secret; MFA stays off until ConfirmMfaEnrollment succeeds.</summary>
    public void StartMfaEnrollment(string base32Secret, long actorUserId, IClock clock)
    {
        MfaPendingSecret = base32Secret;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>Activates MFA from a pending enrollment (call after code verification).</summary>
    public void ConfirmMfaEnrollment(long actorUserId, IClock clock)
    {
        if (MfaPendingSecret is null)
            throw new InvalidOperationException("MFA enrollment စတင်ထားခြင်း မရှိပါ။");
        MfaSecret = MfaPendingSecret;
        MfaPendingSecret = null;
        MfaEnabled = true;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>Turns MFA off and clears all secrets.</summary>
    public void DisableMfa(long actorUserId, IClock clock)
    {
        MfaEnabled = false;
        MfaSecret = null;
        MfaPendingSecret = null;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }
}
