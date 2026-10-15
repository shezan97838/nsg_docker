namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// NIC SMS gateway abstraction for OTP delivery.
/// SOW §3.7: "Mobile OTP is sent via NIC-approved SMS gateway."
/// SOW §4: "NIC-approved SMS service for OTP."
/// 
/// OPEN QUESTION: Exact NIC SMS gateway API contract is not provided in the SOW.
/// This interface defines the minimal contract; implementation will adapt to the actual gateway.
/// </summary>
public interface INicSmsService
{
    /// <summary>Send an OTP message to the specified mobile number.</summary>
    Task<SmsSendResult> SendOtpAsync(string mobileNumber, string otpCode, string? templateId = null, CancellationToken cancellationToken = default);
}

public record SmsSendResult(bool Success, string? MessageId = null, string? ErrorMessage = null);
