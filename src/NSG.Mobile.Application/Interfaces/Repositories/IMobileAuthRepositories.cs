using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum ProfileCreateOutcome { Created = 0, DuplicateNsgNumber, DuplicateMobileNumber, GrantUnavailable }

public enum PendingRequestOutcome { Created = 0, AlreadyPending }

public enum ApprovalDecisionOutcome { Decided = 0, NotFound, AlreadyDecided, PersonnelRecordMissing }

/// <summary>Failed-attempt counter after a wrong MPIN; <see cref="LockedUntilIst"/> is set once the limit is reached.</summary>
public sealed record FailedLoginState(int FailedAttempts, DateTime? LockedUntilIst);

/// <summary>An approval request together with the person's name from PIS (null when the PIS row no longer exists).</summary>
public sealed record ApprovalRequestRow(ApproveMobileUser Request, string? FullName);

/// <summary>Result of an approval decision. <see cref="Request"/> is the updated row on success; <see cref="CurrentStatus"/> is the status found when it was already decided.</summary>
public sealed record ApprovalDecisionResult(ApprovalDecisionOutcome Outcome, ApproveMobileUser? Request = null, MobileAppActivationStatus? CurrentStatus = null);

/// <summary>Data access for <c>mobile_user_profiles</c>. Counter and credential updates are atomic single statements.</summary>
public interface IMobileUserProfileRepository
{
    Task<MobileUserProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);
    Task<MobileUserProfile?> GetByMobileNumberAsync(string mobileNumber, CancellationToken cancellationToken = default);
    Task<MobileUserProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments the failed-attempt counter and locks the profile when it reaches <paramref name="maxAttempts"/>.</summary>
    Task<FailedLoginState> RegisterFailedLoginAsync(long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Clears an elapsed lockout and its failed-attempt counter.</summary>
    Task ClearExpiredLockoutAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RecordSuccessfulLoginAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Replaces the MPIN hash, clears the failed-attempt counter and any lockout, and revokes every session of the profile, in one transaction.</summary>
    Task SetMpinAsync(long id, string mpinHash, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Inserts the profile and consumes the grant in one transaction. Unique-key violations are outcomes, not exceptions.</summary>
    Task<ProfileCreateOutcome> CreateWithGrantAsync(MobileUserProfile profile, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>In one transaction: consumes the reset grant, sets the MPIN (clearing lockout) and invalidates every other outstanding grant of the NSG number. False when the grant was already used.</summary>
    Task<bool> ResetMpinWithGrantAsync(long profileId, string nsgNumber, string mpinHash, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Data access for <c>approve_mobile_users</c>.</summary>
public interface IApproveMobileUserRepository
{
    Task<ApproveMobileUser?> GetPendingByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);

    /// <summary>Inserts a Pending request; the unique <c>pending_nsg_number</c> index turns a concurrent duplicate into <see cref="PendingRequestOutcome.AlreadyPending"/>.</summary>
    Task<PendingRequestOutcome> AddPendingAsync(ApproveMobileUser request, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default);

    /// <summary>Decides a Pending request and sets <c>users_pis.mobile_app_activation_status</c> in one transaction. Only the first decision wins.</summary>
    Task<ApprovalDecisionResult> DecideAsync(long id, MobileAppActivationStatus decision, int approverUserId, string? remarks, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public enum RefreshRotationOutcome { Rotated = 0, Invalid, Expired, ReuseDetected }

/// <summary>Result of presenting a refresh token. On <see cref="RefreshRotationOutcome.Rotated"/> the new token has been stored for the same profile and session.</summary>
public sealed record RefreshRotationResult(RefreshRotationOutcome Outcome, long ProfileId = 0, Guid FamilyId = default);

/// <summary>Data access for <c>mobile_refresh_tokens</c>. Every state change is a conditional single statement or one transaction.</summary>
public interface IMobileRefreshTokenRepository
{
    /// <summary>In one transaction: revokes every live session of the profile (reason NewLogin: one active session per profile) and stores the first token of the new session.</summary>
    Task StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotation: claims the presented token with a conditional update (only one of several concurrent requests wins), then stores
    /// <paramref name="next"/> in the same session. Presenting a token that was already rotated revokes the whole session.
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Revokes the whole session the token belongs to. False when the token is unknown.</summary>
    Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>True while the session has a live token and its profile exists and is active. Checked for every mobile access token.</summary>
    Task<bool> IsSessionActiveAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default);
}

/// <summary>Data access for <c>mobile_auth_grants</c>.</summary>
public interface IMobileAuthGrantRepository
{
    Task AddAsync(MobileAuthGrant grant, CancellationToken cancellationToken = default);
    Task<MobileAuthGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Marks the grant consumed if it still is unused and unexpired. False when another request got there first.</summary>
    Task<bool> TryConsumeAsync(long grantId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Consumes every outstanding grant of an NSG number.</summary>
    Task InvalidateForNsgNumberAsync(string nsgNumber, DateTime nowUtc, CancellationToken cancellationToken = default);
}
