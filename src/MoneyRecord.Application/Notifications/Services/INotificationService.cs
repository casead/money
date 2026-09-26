using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Notifications.Services;

public interface INotificationService
{
    Task NotifyAsync(long shopId, long? userId, NotificationType type,
        string title, string message, string? data, CancellationToken ct = default);
}
