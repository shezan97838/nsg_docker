using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Admin Portal account of a person. Table <c>admin_profiles</c>. Separate from <see cref="MobileUserProfile"/> and from the
/// staff <see cref="User"/>: an admin identity is never a mobile identity.
///
/// <see cref="NsgNumber"/> points at the personnel record in PIS (<see cref="UserPis"/>), which stays the only source of the
/// registered mobile number, date of birth and every other personnel detail. Nothing of that is copied here; the OTP destination
/// and date of birth are always read from PIS at the moment they are needed. As with <see cref="MobileUserProfile"/> there is
/// deliberately no foreign key to <c>users_pis</c> (a key on <c>nsg_number</c> would add an alternate key to the client-approved
/// PIS table); the link is checked by the provisioning and activation services.
///
/// Credentials are stored only as a secure hash (<see cref="PasswordHash"/>), null until the person sets a password during
/// activation. A Super Admin never sets or sees a password. Timestamps are IST instants (see docs/DATABASE-STANDARDS.md).
/// </summary>
public class AdminProfile
{
    /// <summary>Surrogate key (MySQL BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>NSG number, stored exactly as in PIS. Required, unique (one admin account per person).</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Fixed role (INT, same type as <see cref="AdminRole.Id"/>). Required.</summary>
    public int RoleId { get; set; }
    public AdminRole? Role { get; set; }

    /// <summary>Hash of the password (never the password). Null until the person sets it during activation.</summary>
    public string? PasswordHash { get; set; }

    public AdminActivationStatus ActivationStatus { get; set; } = AdminActivationStatus.PendingActivation;

    /// <summary>False blocks sign-in, token refresh and every protected admin API.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed password/OTP attempts; reset on successful sign-in.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>End of the current lockout (IST). Null when not locked.</summary>
    public DateTime? LockoutEndIst { get; set; }

    public DateTime? LastLoginAtIst { get; set; }

    // ── Audit ──

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    /// <summary><see cref="Id"/> of the Super Admin who created the account. Null for the bootstrap account. No FK.</summary>
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public long? UpdatedBy { get; set; }
}
