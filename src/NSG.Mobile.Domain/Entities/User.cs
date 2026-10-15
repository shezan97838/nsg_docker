using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Core user entity representing NSG personnel.
/// SOW §3.1 (PIS): Name, Rank, Force No., Contact Information.
/// SOW §3.7 (Onboarding): Registration with Name, Rank, Force Number, Mobile Number.
/// SOW §3.8 (Security): MFA, IT approval, account lifecycle.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Full name of the NSG personnel. SOW §3.1, §3.7.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Rank designation. SOW §3.1, §3.7.</summary>
    public string Rank { get; set; } = string.Empty;

    /// <summary>Unique Force Number identifier. SOW §3.1, §3.7.</summary>
    public string ForceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Normalized mobile number (E.164 format preferred).
    /// This is the login identifier. SOW §3.7 Step 1.
    /// </summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Password hash — never stored in plaintext. SOW §3.7 Step 3, §3.8.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Account lifecycle status. SOW §3.7 onboarding flow.</summary>
    public UserStatus Status { get; set; } = UserStatus.PendingOtpVerification;

    /// <summary>Authorization role. SOW §3.5, §3.7, §3.8.</summary>
    public UserRole Role { get; set; } = UserRole.Employee;

    /// <summary>Indicates whether the account is currently active.</summary>
    public bool IsActive { get; set; }

    /// <summary>User ID of the IT Cell administrator who approved this account. SOW §3.7 Step 2.</summary>
    public int? ApprovedBy { get; set; }

    /// <summary>Timestamp of IT approval. SOW §3.7 Step 2.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Last successful login timestamp for security tracking.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Consecutive failed login attempt counter. SOW §3.8: account lockout support.
    /// Reset on successful login.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Account lockout end time. Null if not locked.</summary>
    public DateTime? LockoutEndAt { get; set; }

    /// <summary>Optimistic concurrency token for EF Core.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime UpdatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    // Navigation properties
    public ICollection<Device> Devices { get; set; } = new List<Device>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
