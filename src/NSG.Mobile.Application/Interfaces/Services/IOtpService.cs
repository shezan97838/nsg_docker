using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Services;

/// <summary>
/// OTP generation and verification service abstraction.
/// SOW §3.7: OTP via NIC-approved SMS gateway.
/// SOW §3.8: OTP as part of MFA.
/// </summary>
public interface IOtpService
{
    /// <summary>Generate a cryptographically secure OTP and send it via SMS.</summary>
    Task<OtpGenerationResult> GenerateAndSendAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>Verify an OTP against the stored hash, enforcing attempt limits and expiration.</summary>
    Task<OtpVerificationResult> VerifyAsync(Guid referenceId, string otpCode, OtpPurpose expectedPurpose, CancellationToken cancellationToken = default);
}

/// <param name="IsDemo">True when the OTP is the fixed demonstration code and no SMS was sent.</param>
public record OtpGenerationResult(bool Success, Guid? ReferenceId, string? ErrorCode = null, string? ErrorMessage = null, DateTime? ExpiresAtIst = null, bool IsDemo = false);
public record OtpVerificationResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null);
