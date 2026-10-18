using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Result of inserting an admin profile; a unique-key violation is an outcome, not an exception.</summary>
public enum AdminCreateOutcome { Created = 0, DuplicateNsgNumber }

/// <summary>Result of an enable / disable request.</summary>
public enum AdminStateChangeOutcome { Changed = 0, NotFound, AlreadyInTargetState }

/// <summary>One list row: the account, its role, and the name held in PIS (null when PIS has no name).</summary>
public sealed record AdminAccountRow(AdminProfile Profile, string? FullName);

/// <summary>Filters of the account list. Every member is optional.</summary>
public sealed record AdminAccountFilter(string? RoleCode = null, AdminActivationStatus? ActivationStatus = null, bool? IsActive = null, string? Search = null);

/// <summary>Data access for <c>admin_profiles</c>. Profiles are never deleted; accounts are deactivated.</summary>
public interface IAdminProfileRepository
{
    /// <summary>Untracked profile with its <see cref="AdminProfile.Role"/> loaded; null when absent.</summary>
    Task<AdminProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked profile with its role loaded, found by NSG number; null when absent.</summary>
    Task<AdminProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);

    Task<AdminAccountRow?> GetRowByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AdminAccountRow> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, AdminAccountFilter filter, CancellationToken cancellationToken = default);

    Task<AdminCreateOutcome> AddAsync(AdminProfile profile, CancellationToken cancellationToken = default);

    /// <summary>True when at least one Super Admin account exists, active or not (bootstrap guard).</summary>
    Task<bool> AnySuperAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>Number of enabled, activated Super Admin accounts (last-Super-Admin guard).</summary>
    Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the password hash and the Active status in one conditional statement that only matches an enabled account that is still
    /// PendingActivation, so of two simultaneous activations exactly one wins. Clears failed attempts and lockout.
    /// </summary>
    Task<bool> ActivateAsync(long id, string passwordHash, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables the account (enabling also clears lockout).</summary>
    Task<AdminStateChangeOutcome> SetEnabledAsync(long id, bool enabled, long actorId, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments the failed-attempt counter and locks the account when it reaches <paramref name="maxAttempts"/>.</summary>
    Task<FailedLoginState> RegisterFailedLoginAsync(long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowIst, CancellationToken cancellationToken = default);

    Task ClearExpiredLockoutAsync(long id, DateTime nowIst, CancellationToken cancellationToken = default);

    Task RecordSuccessfulLoginAsync(long id, DateTime nowIst, CancellationToken cancellationToken = default);
}

/// <summary>Read access to the six fixed roles. Nothing here writes <c>admin_roles</c>.</summary>
public interface IAdminRoleRepository
{
    Task<AdminRole?> GetByCodeAsync(string roleCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminRole>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Admin Portal sessions (refresh-token families). Same rotation rules as the mobile sessions, separate table.</summary>
public interface IAdminRefreshTokenRepository
{
    /// <summary>Ends every live session of the profile and stores the first token of the new one, in one transaction.</summary>
    Task StartSessionAsync(AdminRefreshToken first, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Atomically exchanges the presented token for <paramref name="next"/>; <see cref="RefreshRotationResult.ProfileId"/> is the admin profile id.</summary>
    Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, AdminRefreshToken next, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Revokes the whole session of a token. False when the token is unknown.</summary>
    Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowIst, CancellationToken cancellationToken = default);

    Task RevokeAllForProfileAsync(long adminProfileId, string reason, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>True when the session is live and its profile is enabled, activated and still holds <paramref name="roleCode"/> (checked on every admin request).</summary>
    Task<bool> IsSessionActiveAsync(Guid sessionId, long adminProfileId, string roleCode, CancellationToken cancellationToken = default);
}
