namespace MoneyRecord.Domain.Entities;

public class FcmToken
{
    public long Id { get; private set; }
    public long UserId { get; private set; }
    public long ShopId { get; private set; }
    public string Token { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private FcmToken() { }

    public static FcmToken Create(long userId, long shopId, string token, DateTime utc)
    {
        return new FcmToken
        {
            UserId = userId,
            ShopId = shopId,
            Token = token,
            CreatedAt = utc,
            LastUsedAt = utc
        };
    }

    public void Touch(DateTime utc) => LastUsedAt = utc;
}
