namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Push notification service abstraction.
/// SOW §3.5: "Push Notifications via FCM for Android and APNs for iOS."
/// SOW §3.5: "Admin-controlled broadcasts, Targeted notifications (by Office/Group, Rank, or individual)."
/// 
/// OPEN QUESTION: FCM/APNs configuration and credential details not provided in the SOW.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>Send a notification to a specific device.</summary>
    Task<NotificationResult> SendToDeviceAsync(string pushToken, string title, string body, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);

    /// <summary>Send a notification to a topic (for group/rank targeting).</summary>
    Task<NotificationResult> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default);
}

public record NotificationResult(bool Success, string? MessageId = null, string? ErrorMessage = null);
