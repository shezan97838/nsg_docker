namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Refresh token of an Admin Portal session. Table <c>admin_refresh_tokens</c>. Belongs to an <see cref="AdminProfile"/> and is
/// independent of <c>mobile_refresh_tokens</c> and the staff <c>refresh_tokens</c>, so an admin session can never be refreshed
/// through (or confused with) a mobile one.
///
/// Only the SHA-256 hash of the token is stored. Tokens of one login form a family (<see cref="FamilyId"/>, also the <c>sid</c>
/// claim of the access token): each refresh revokes the presented token and issues the next one in the same family; presenting an
/// already-used token revokes the whole family. All timestamps are IST.
/// </summary>
public class AdminRefreshToken
{
    /// <summary>Surrogate key (MySQL BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>Owning admin profile (BIGINT, same type as <see cref="AdminProfile.Id"/>).</summary>
    public long AdminProfileId { get; set; }

    /// <summary>SHA-256 of the token, base64. Unique. Never the token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Session identifier shared by every token of one login.</summary>
    public Guid FamilyId { get; set; }

    public DateTime ExpiresAtIst { get; set; }
    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public string? CreatedByIp { get; set; }

    /// <summary>When the token was used, logged out or revoked (IST). Null while it is the live token of its session.</summary>
    public DateTime? RevokedAtIst { get; set; }

    /// <summary>Why it was revoked: see <see cref="AdminSessionRevokeReasons"/>.</summary>
    public string? RevokeReason { get; set; }

    /// <summary>When a revoked token was presented again (theft signal); the whole family was revoked at that moment.</summary>
    public DateTime? ReuseDetectedAtIst { get; set; }
}

/// <summary>Values stored in <see cref="AdminRefreshToken.RevokeReason"/>.</summary>
public static class AdminSessionRevokeReasons
{
    public const string Rotated = "Rotated";
    public const string Logout = "Logout";
    public const string NewLogin = "NewLogin";
    public const string ReuseDetected = "ReuseDetected";
    public const string AccountDeactivated = "AccountDeactivated";
    public const string PasswordChanged = "PasswordChanged";
}
