namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Refresh token record supporting secure token rotation and reuse detection.
/// SOW §3.8: JWT/OAuth 2.0 authentication with configurable session timeout.
/// 
/// Tokens are stored as SHA-256 hashes — never in plaintext.
/// FamilyId groups tokens in a rotation chain for reuse detection.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>Owning user ID.</summary>
    public int UserId { get; set; }

    /// <summary>Device that issued this token — enforces device binding.</summary>
    public int DeviceId { get; set; }

    /// <summary>SHA-256 hash of the opaque refresh token. Never store plaintext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Token family identifier — all tokens in a rotation chain share the same FamilyId.
    /// Used for reuse detection: if a revoked token is reused, the entire family is revoked.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>Token expiration timestamp (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Timestamp when this token was revoked. Null if still valid.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Points to the replacement token after rotation.
    /// Null if this is the latest token in the chain.
    /// </summary>
    public int? ReplacedByTokenId { get; set; }

    /// <summary>IP address that created this token.</summary>
    public string? CreatedByIp { get; set; }

    /// <summary>IP address that revoked this token.</summary>
    public string? RevokedByIp { get; set; }

    /// <summary>Reason for revocation (e.g., "Rotated", "Logout", "ReuseDetected").</summary>
    public string? RevokeReason { get; set; }

    /// <summary>Timestamp when reuse of this already-revoked token was detected.</summary>
    public DateTime? ReuseDetectedAt { get; set; }

    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    /// <summary>Whether this token is currently valid (not expired, not revoked).</summary>
    public bool IsActive => RevokedAt == null && NSG.Mobile.Domain.Common.IndianStandardTime.Now < ExpiresAt;

    // Navigation properties
    public User User { get; set; } = null!;
    public Device Device { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }
}
