using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Mobile-app account of a person whose activation request was approved. Table <c>mobile_user_profiles</c>.
/// Looked up first at login/registration by NSG number, personal mobile number and date of birth.
///
/// There is deliberately no foreign key to <see cref="UserPis"/>: when no profile exists the personnel record is located
/// in PIS by <see cref="NsgNumber"/> (the same string identifier PIS uses; it is never used as a database key here).
/// Credentials are stored only as secure hashes (<see cref="MpinHash"/>, <see cref="PasswordHash"/>); never plaintext.
/// The MPIN is the primary login method. Device biometrics (later) are verified on the device and reach the server only as a
/// device-bound assertion (ADR-007); biometric data is never stored and is never a server-side password.
///
/// All timestamps are UTC instants; convert to Asia/Kolkata (IST, UTC+05:30) only at the API/UI boundary. Never rely on the
/// server's local timezone. <see cref="DateOfBirth"/> is a calendar date (<see cref="DateOnly"/>) with no timezone.
/// </summary>
public class MobileUserProfile
{
    /// <summary>Surrogate key (MySQL BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>NSG number, stored exactly as in PIS. Required, unique.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Personal mobile number: exactly ten digits starting 6-9, no country code (same format as PIS). Required, unique.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Date of birth. Required. NOT unique: different people can share a birth date.</summary>
    public DateOnly DateOfBirth { get; set; }

    /// <summary>
    /// Authorization role. Mobile registration always assigns <see cref="UserRole.Employee"/> on the server; a role supplied by
    /// a client is never accepted. The column is a closed int set of the existing <see cref="UserRole"/> values so web access or
    /// other roles can be added later, but holding a role here grants no permission by itself.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Employee;

    /// <summary>Hash of the MPIN (never the MPIN), the primary login credential. Null until the MPIN is set up.</summary>
    public string? MpinHash { get; set; }

    /// <summary>Hash of the password (never the password), a secondary credential. Null until a password is set up.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>False blocks login; the user is told to contact IT support.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed MPIN/password attempts; reset on successful login.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>End of the current lockout (IST). Null when not locked.</summary>
    public DateTime? LockedUntilIst { get; set; }

    public DateTime? LastLoginAtIst { get; set; }

    // ── Audit ──

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    /// <summary>User id of the creator (no FK, following <see cref="UserPis.CreatedBy"/>).</summary>
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public int? UpdatedBy { get; set; }
}
