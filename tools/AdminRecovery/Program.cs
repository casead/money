// Admin recovery — run from repo root:
//   dotnet run --project backend/tools/AdminRecovery -- <newPassword> [--keep-mfa]
// Resets the bootstrap admin's password (and clears MFA unless --keep-mfa).
using System.Security.Cryptography;
using Npgsql;

var password = args.FirstOrDefault(a => !a.StartsWith("--"))
    ?? throw new ArgumentException("Usage: dotnet run ... -- <newPassword> [--keep-mfa]");
var keepMfa = args.Contains("--keep-mfa");

if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
    throw new ArgumentException("Password: ၈+ လုံး · စာလုံး+ဂဏန်း");

byte[] salt = RandomNumberGenerator.GetBytes(32);
byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA512, 64);
var stored = $"pbkdf2-sha512$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

var cs = Environment.GetEnvironmentVariable("MONEYRECORD_IT_ADMIN")
    ?? "Host=aws-0-ap-northeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.aoipgqgjbaaoktjrlncq;Password=erBK8sA0s8WxfF5T;SSL Mode=Require";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

var sql = "UPDATE \"Users\" SET \"PasswordHash\" = @hash, \"FailedLoginCount\" = 0, \"LockedUntilUtc\" = NULL";
if (!keepMfa)
    sql += ", \"MfaEnabled\" = false, \"MfaSecret\" = NULL, \"MfaPendingSecret\" = NULL";
sql += " WHERE \"Username\" = 'admin'";

await using var cmd = new NpgsqlCommand(sql, conn);
cmd.Parameters.AddWithValue("hash", stored);
var rows = await cmd.ExecuteNonQueryAsync();

await using var revoke = new NpgsqlCommand("""
    UPDATE "RefreshTokens" SET "RevokedAtUtc" = NOW()
     WHERE "UserId" = (SELECT "Id" FROM "Users" WHERE "Username" = 'admin')
       AND "RevokedAtUtc" IS NULL
    """, conn);
await revoke.ExecuteNonQueryAsync();

Console.WriteLine(rows == 1
    ? $"OK — admin password reset{(keepMfa ? " (MFA kept)" : " + MFA cleared")}; all sessions revoked."
    : "No 'admin' row updated — check username.");
