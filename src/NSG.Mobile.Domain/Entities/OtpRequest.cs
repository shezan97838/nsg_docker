using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// OTP request record for tracking OTP lifecycle, attempt limits, and expiration.
/// SOW §3.7 Step 1: "Mobile OTP is sent via NIC-approved SMS gateway."
/// SOW §3.8: "Multi-Factor Authentication (MFA): Password + OTP + Biometric."
/// 
/// OTP values are stored as hashes — never in plaintext.
/// Different purposes (Registration, Login, etc.) prevent cross-purpose reuse.
/// </summary>
public class OtpRequest
{
    public int Id { get; set; }

    /// <summary>Client-facing reference ID (not the OTP itself).</summary>
    public Guid ReferenceId { get; set; } = Guid.NewGuid();

    /// <summary>Normalized mobile number this OTP was sent to.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Hash of the OTP value — never store plaintext.</summary>
    public string OtpHash { get; set; } = string.Empty;

    /// <summary>Purpose classification to prevent cross-purpose OTP reuse.</summary>
    public OtpPurpose Purpose { get; set; }

    /// <summary>Number of verification attempts made against this OTP.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Whether this OTP has been successfully verified.</summary>
    public bool IsVerified { get; set; }

    /// <summary>Expiration timestamp (UTC). Short TTL enforced.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Timestamp when OTP was successfully verified.</summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
