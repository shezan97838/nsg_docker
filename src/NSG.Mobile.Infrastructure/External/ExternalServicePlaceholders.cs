using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Interfaces.Infrastructure;

namespace NSG.Mobile.Infrastructure.External;

/// <summary>
/// Stand-in for the NIC/BSNL SMS gateway, which is not available yet. It sends nothing and says so: every call FAILS, so a
/// deployment without a real gateway cannot pretend to deliver OTPs, and the OTP is never logged.
/// For presentations use <c>Otp:Mode=Demo</c> (guarded by <c>OtpModeGuard</c>) instead of faking a send here.
/// OPEN QUESTION: the gateway API contract is not provided; replace this registration with the real provider.
/// </summary>
public class NicSmsServicePlaceholder : INicSmsService
{
    private readonly ILogger<NicSmsServicePlaceholder> _logger;

    public NicSmsServicePlaceholder(ILogger<NicSmsServicePlaceholder> logger) => _logger = logger;

    public Task<SmsSendResult> SendOtpAsync(string mobileNumber, string otpCode, string? templateId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogError("No SMS gateway is configured; the OTP was not sent. Configure a provider, or use Otp:Mode=Demo in an approved non-production environment.");
        return Task.FromResult(new SmsSendResult(false, ErrorMessage: "SMS gateway is not configured."));
    }
}

/// <summary>
/// Placeholder FCM push notification service.
/// OPEN QUESTION: FCM configuration and credentials not provided in SOW.
/// </summary>
public class FcmPushNotificationServicePlaceholder : IPushNotificationService
{
    private readonly ILogger<FcmPushNotificationServicePlaceholder> _logger;

    public FcmPushNotificationServicePlaceholder(ILogger<FcmPushNotificationServicePlaceholder> logger) => _logger = logger;

    public Task<NotificationResult> SendToDeviceAsync(string pushToken, string title, string body, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PLACEHOLDER] Push notification to device: {Title}", title);
        return Task.FromResult(new NotificationResult(true, MessageId: Guid.NewGuid().ToString()));
    }

    public Task<NotificationResult> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PLACEHOLDER] Push notification to topic {Topic}: {Title}", topic, title);
        return Task.FromResult(new NotificationResult(true, MessageId: Guid.NewGuid().ToString()));
    }
}

/// <summary>
/// Placeholder HRMS integration service.
/// OPEN QUESTION: Exact HRMS API contract not provided in SOW.
/// </summary>
public class HrmsServicePlaceholder : IHrmsService
{
    private readonly ILogger<HrmsServicePlaceholder> _logger;

    public HrmsServicePlaceholder(ILogger<HrmsServicePlaceholder> logger) => _logger = logger;

    public Task<HrmsVerificationResult> VerifyPersonnelAsync(string forceNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PLACEHOLDER] HRMS verification for force number {ForceNumber}", forceNumber);
        // Placeholder returns "found" for development. Production will call actual HRMS API.
        return Task.FromResult(new HrmsVerificationResult(Found: true));
    }
}

/// <summary>
/// Placeholder Payroll integration service.
/// OPEN QUESTION: Exact Payroll API contract not provided in SOW.
/// </summary>
public class PayrollServicePlaceholder : IPayrollService
{
    private readonly ILogger<PayrollServicePlaceholder> _logger;

    public PayrollServicePlaceholder(ILogger<PayrollServicePlaceholder> logger) => _logger = logger;
}
