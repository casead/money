namespace MoneyRecord.Application.Notifications.Services;

public interface IFcmSender
{
    Task SendAsync(IReadOnlyList<string> tokens, string title, string body, string? data);
}
