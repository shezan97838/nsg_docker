using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum ProfileCreateOutcome { Created = 0, DuplicateNsgNumber, DuplicateMobileNumber, GrantUnavailable }

public enum PendingRequestOutcome { Created = 0, AlreadyPending }

public enum ApprovalDecisionOutcome { Decided = 0, NotFound, AlreadyDecided, PersonnelRecordMissing, NotOwner }

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

    /// <summary>The most recently approved request of the NSG number, or null when none was ever approved.</summary>
    Task<ApproveMobileUser?> GetLatestApprovedByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);

    /// <summary>Inserts a Pending request; the unique <c>pending_nsg_number</c> index turns a concurrent duplicate into <see cref="PendingRequestOutcome.AlreadyPending"/>.</summary>
    Task<PendingRequestOutcome> AddPendingAsync(ApproveMobileUser request, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the requests whose personnel record is CURRENTLY assigned to <paramref name="ownerAdminId"/> (<c>users_pis.assigned_to</c>, resolved by
    /// NSG number in the same query). The ownership condition is part of the query, so the count, the filters and the page all share it; requests of
    /// unassigned, other admins' or missing personnel never appear.
    /// </summary>
    Task<(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount)> GetPagedAsync(
        long ownerAdminId, int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides a Pending request and sets <c>users_pis.mobile_app_activation_status</c> in one transaction. Only the first decision wins.
    /// The personnel row is locked FIRST, conditional on <c>assigned_to = <paramref name="adminProfileId"/></c>, so the ownership that authorises the
    /// decision cannot change until the transaction ends (<see cref="ApprovalDecisionOutcome.NotOwner"/> otherwise; nothing is changed).
    /// <paramref name="adminProfileId"/> must fit the int <c>approved_by</c> column (the caller checks); it is never truncated.
    /// </summary>
    Task<ApprovalDecisionResult> DecideAsync(long id, MobileAppActivationStatus decision, long adminProfileId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default);
}

public enum RefreshRotationOutcome { Rotated = 0, Invalid, Expired, ReuseDetected }

/// <summary>
/// Result of presenting a refresh token. On <see cref="RefreshRotationOutcome.Rotated"/> the new token has been stored for the same profile and session.
/// <see cref="RevokeReason"/> is the reason the presented token had been revoked (set for <see cref="RefreshRotationOutcome.Invalid"/> when it was revoked).
/// </summary>
public sealed record RefreshRotationResult(RefreshRotationOutcome Outcome, long ProfileId = 0, Guid FamilyId = default, string? RevokeReason = null);

/// <summary>Why a mobile access token's session is (not) usable. <see cref="AdminBlocked"/> is reported to the app as <c>SURAKSHA_1001</c>, <see cref="Replaced"/> as <c>SURAKSHA_1002</c>.</summary>
public enum MobileSessionState
{
    /// <summary>A live token exists, the profile exists, is active and is not blocked.</summary>
    Active = 0,

    /// <summary>Unusable for any other reason: unknown session, logout, rotation, MPIN change/reset, reuse, inactive profile (or replaced while the profile is blocked).</summary>
    Revoked,

    /// <summary>The profile carries a PIS Admin block, or the session's latest token was revoked with reason <c>AdminBlocked</c>.</summary>
    AdminBlocked,

    /// <summary>
    /// The session's latest token was revoked with reason <c>NewLogin</c>: a newer successful login of the same profile replaced it
    /// (reported to the app as <c>SURAKSHA_1002</c>). Never returned while the profile carries a PIS Admin block.
    /// </summary>
    Replaced
}

/// <summary>Outcome of starting a session: it is only stored when the profile is still active and not blocked at that moment.</summary>
public enum SessionStartOutcome { Started = 0, AdminBlocked, ProfileInactive }

/// <summary>Data access for <c>mobile_refresh_tokens</c>. Every state change is a conditional single statement or one transaction.</summary>
public interface IMobileRefreshTokenRepository
{
    /// <summary>
    /// In one transaction: confirms the profile is still active and not blocked (taking its row lock, so a concurrent block either
    /// commits first and nothing is stored, or waits and then revokes the new session), revokes every live session of the profile
    /// (reason NewLogin: one active session per profile) and stores the first token of the new session.
    /// </summary>
    Task<SessionStartOutcome> StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotation: claims the presented token with a conditional update (only one of several concurrent requests wins), then stores
    /// <paramref name="next"/> in the same session. Presenting a token that was already rotated revokes the whole session, unless the
    /// session had already been replaced by a newer login: that is reported as <see cref="RefreshRotationOutcome.Invalid"/> with
    /// <c>RevokeReason = NewLogin</c> (never as reuse), and nothing is changed.
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Revokes the whole session the token belongs to. False when the token is unknown.</summary>
    Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>True while the session has a live token and its profile exists and is active. Checked for every mobile access token.</summary>
    Task<bool> IsSessionActiveAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The state of a session for an access token: looks at the latest token row of the session (a rotated row is not the end of a
    /// session) and at the profile's block marker, which is authoritative for the current block. The default maps the boolean check,
    /// for implementations that cannot tell why a session ended.
    /// </summary>
    async Task<MobileSessionState> GetSessionStateAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default)
        => await IsSessionActiveAsync(sessionId, profileId, cancellationToken) ? MobileSessionState.Active : MobileSessionState.Revoked;
}

public enum MobileAccessChangeOutcome
{
    /// <summary>The state was changed by this call.</summary>
    Changed = 0,

    /// <summary>Already in the requested state (block: already blocked; restore: already active). Nothing was changed.</summary>
    NoChange,

    /// <summary>Refused because the profile is inactive for another reason (block), or inactive but not blocked through this mechanism (restore).</summary>
    Refused,

    NotFound
}

/// <summary>Data access for the PIS Admin block / restore of mobile access (<c>mobile_user_profiles</c> + <c>mobile_refresh_tokens</c>).</summary>
public interface IMobileAccessRepository
{
    /// <summary>
    /// In one transaction: sets IsActive = false and the block marker (who, when, why) with a conditional update, and revokes every live
    /// refresh token of the profile with reason <c>AdminBlocked</c>. Idempotent: an already blocked profile is left unchanged (its live
    /// tokens, if any, are still revoked). A profile that is inactive for another reason is refused.
    /// </summary>
    Task<MobileAccessChangeOutcome> BlockAsync(long profileId, int adminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditional update: only a profile carrying the block marker is restored (IsActive = true, marker cleared). Never touches
    /// refresh tokens, so no old session is revived. A profile that is inactive without the marker is refused, an active one is a no-op.
    /// </summary>
    Task<MobileAccessChangeOutcome> RestoreAsync(long profileId, int adminId, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>True when the profile has at least one refresh token that is neither revoked nor expired.</summary>
    Task<bool> HasLiveSessionAsync(long profileId, DateTime nowIst, CancellationToken cancellationToken = default);
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
