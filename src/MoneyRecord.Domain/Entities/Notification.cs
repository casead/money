namespace MoneyRecord.Domain.Entities;

public enum NotificationType : byte
{
    CreditCreated = 1,
    CreditSettled = 2,
    CreditReminder = 3
}

public class Notification
{
    public long Id { get; private set; }
    public long ShopId { get; private set; }
    public long? UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public string? Data { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Notification() { }

    public static Notification Create(long shopId, long? userId, NotificationType type,
        string title, string message, string? data, DateTime utc)
    {
        return new Notification
        {
            ShopId = shopId,
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            Data = data,
            IsRead = false,
            CreatedAt = utc
        };
    }

    public void MarkRead() => IsRead = true;
}
