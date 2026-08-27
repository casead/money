using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the initial Admin account on first run (bootstrap — no login possible before it exists).
/// Username: admin / Password from env MONEYRECORD_ADMIN_PASSWORD (default dev-only).
/// </summary>
public static class AdminSeeder
{
    public const string DefaultAdminUsername = "admin";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MoneyRecordDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var clock = services.GetRequiredService<Application.Common.Interfaces.IClock>();
        var logger = services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MoneyRecordDbContext>>();

        await db.Database.MigrateAsync();

        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == DefaultAdminUsername))
        {
            var password = Environment.GetEnvironmentVariable("MONEYRECORD_ADMIN_PASSWORD")
                ?? "Admin@12345"; // dev default — MUST be changed in production

            var admin = User.Create(
                DefaultAdminUsername,
                hasher.Hash(password),
                "System Administrator",
                roleId: 1,
                actorUserId: 0,
                clock);

            // System bootstrap: no creating actor
            typeof(User).GetProperty(nameof(User.CreatedByUserId))!
                .SetValue(admin, null);

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            logger.LogInformation("Seeded initial admin account '{Username}'.", DefaultAdminUsername);
        }
    }
}
