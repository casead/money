using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using MoneyRecord.Application.Notifications.Services;

namespace MoneyRecord.Infrastructure.Services;

public sealed class FirebaseFcmSender : IFcmSender
{
    private readonly ILogger<FirebaseFcmSender> _logger;

    public FirebaseFcmSender(ILogger<FirebaseFcmSender> logger)
    {
        _logger = logger;
        // Firebase push DISABLED — local notification only.
        // To re-enable: uncomment FirebaseApp.Create below and set env vars.
        // if (FirebaseApp.DefaultInstance is null)
        // {
        //     var json = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");
        //     if (!string.IsNullOrEmpty(json))
        //     {
        //         FirebaseApp.Create(new AppOptions
        //         {
        //             Credential = GoogleCredential.FromJson(json)
        //         });
        //     }
        // }
    }

    public Task SendAsync(IReadOnlyList<string> tokens, string title, string body, string? data)
    {
        // Firebase push DISABLED — no-op.
        return Task.CompletedTask;
    }
}
