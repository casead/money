using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Notifications.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationService(IMoneyRecordDbContext db, IClock clock,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _clock = clock;
        _scopeFactory = scopeFactory;
    }

    public async Task NotifyAsync(long shopId, long? userId, NotificationType type,
        string title, string message, string? data, CancellationToken ct = default)
    {
        var notification = Notification.Create(shopId, userId, type, title, message, data, _clock.UtcNow);
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        // Firebase push DISABLED — local notification only.
        // To re-enable FCM, uncomment below:
        // _ = Task.Run(async () =>
        // {
        //     try
        //     {
        //         using var scope = _scopeFactory.CreateScope();
        //         var fcmSender = scope.ServiceProvider.GetRequiredService<IFcmSender>();
        //         var db = scope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        //         var tokens = await db.FcmTokens.AsNoTracking()
        //             .Where(t => t.ShopId == shopId)
        //             .Select(t => t.Token)
        //             .ToListAsync();
        //         if (tokens.Count > 0)
        //             await fcmSender.SendAsync(tokens, title, message, data);
        //     }
        //     catch { /* best-effort */ }
        // });
    }
}
