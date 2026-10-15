using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Registered device bound to a user for single-device enforcement.
/// SOW §3.7 Step 3: "Device binding is completed (IMEI + device token)."
/// SOW §3.8: "Single device binding per user."
/// 
/// Note: Raw IMEI is not used due to modern OS restrictions.
/// A hashed device identifier + optional cryptographic key binding is used instead.
/// See ADR-006.
/// </summary>
public class Device
{
    public int Id { get; set; }

    /// <summary>Owning user ID.</summary>
    public int UserId { get; set; }

    /// <summary>Platform: Android or iOS. SOW §4.</summary>
    public DevicePlatform Platform { get; set; }

    /// <summary>Human-readable device name (e.g., "Pixel 8 Pro").</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Hashed device identifier — NOT raw IMEI.
    /// Used for binding verification.
    /// </summary>
    public string DeviceIdentifierHash { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Device public key for cryptographic device binding.
    /// Private key remains on device secure storage.
    /// See ADR-006.
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>FCM/APNs push notification token. SOW §3.5.</summary>
    public string? PushToken { get; set; }

    /// <summary>Device lifecycle status.</summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Pending;

    /// <summary>Mobile application version.</summary>
    public string? AppVersion { get; set; }

    /// <summary>Operating system version.</summary>
    public string? OsVersion { get; set; }

    /// <summary>Last activity timestamp.</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Last known IP address (for security audit).</summary>
    public string? LastIpAddress { get; set; }

    /// <summary>Timestamp when device was revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime UpdatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
