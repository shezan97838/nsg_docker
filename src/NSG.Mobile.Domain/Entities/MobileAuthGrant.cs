using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Short-lived, purpose-specific, single-use grant that carries the proof of a completed step (OTP verified, registration
/// submitted) from one mobile-auth endpoint to the next. Table <c>mobile_auth_grants</c>.
///
/// Only the SHA-256 hash of the grant secret is stored; the secret itself is shown to the client once. A grant is bound to the
/// NSG number and mobile number it was issued for, expires quickly, and is consumed atomically. All timestamps are UTC.
/// </summary>
public class MobileAuthGrant
{
    /// <summary>Surrogate key (MySQL BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>SHA-256 of the grant secret, base64. Unique. Never the secret.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public MobileGrantPurpose Purpose { get; set; }

    /// <summary>NSG number the grant was issued for (as stored in PIS).</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Personal mobile number the grant was issued for: ten digits, no country code.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    public DateTime ExpiresAtIst { get; set; }

    /// <summary>When the grant was used or invalidated (IST). Null while it is still usable.</summary>
    public DateTime? ConsumedAtIst { get; set; }

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
