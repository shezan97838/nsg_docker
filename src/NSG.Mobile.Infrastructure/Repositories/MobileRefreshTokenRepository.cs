using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class MobileRefreshTokenRepository : IMobileRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public MobileRefreshTokenRepository(AppDbContext context) => _context = context;

    public async Task<SessionStartOutcome> StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Take the profile's row lock first. A block that has committed leaves this update matching nothing; a block that is still
            // running makes it wait for the commit. Either way the read below sees the committed state, so a session is never stored
            // for a profile whose block has committed. (The decision is the read, not the affected-row count.)
            await _context.MobileUserProfiles
                .Where(p => p.Id == first.ProfileId && p.IsActive && p.AccessBlockedAtIst == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastLoginAtIst, (DateTime?)nowUtc), cancellationToken);

            var profile = await _context.MobileUserProfiles.AsNoTracking()
                .Where(p => p.Id == first.ProfileId)
                .Select(p => new { p.IsActive, Blocked = p.AccessBlockedAtIst != null })
                .FirstOrDefaultAsync(cancellationToken);
            if (profile == null || !profile.IsActive || profile.Blocked)
            {
                await transaction.RollbackAsync(cancellationToken);
                return profile is { Blocked: true } ? SessionStartOutcome.AdminBlocked : SessionStartOutcome.ProfileInactive;
            }

            await _context.MobileRefreshTokens
                .Where(t => t.ProfileId == first.ProfileId && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.NewLogin), cancellationToken);

            _context.MobileRefreshTokens.Add(first);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SessionStartOutcome.Started;
        });
    }

    public async Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Claim the presented token. Of any number of simultaneous requests carrying the same token, exactly one updates a row.
            int claimed = await _context.MobileRefreshTokens
                .Where(t => t.TokenHash == presentedTokenHash && t.RevokedAtIst == null && t.ExpiresAtIst > nowUtc)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.Rotated), cancellationToken);

            var presented = await _context.MobileRefreshTokens.AsNoTracking()
                .Where(t => t.TokenHash == presentedTokenHash)
                .Select(t => new { t.ProfileId, t.FamilyId, t.RevokedAtIst, t.RevokeReason })
                .FirstOrDefaultAsync(cancellationToken);

            if (presented == null) return new RefreshRotationResult(RefreshRotationOutcome.Invalid);

            if (claimed == 1)
            {
                next.ProfileId = presented.ProfileId;
                next.FamilyId = presented.FamilyId;
                _context.MobileRefreshTokens.Add(next);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new RefreshRotationResult(RefreshRotationOutcome.Rotated, presented.ProfileId, presented.FamilyId);
            }

            if (presented.RevokedAtIst != null && presented.RevokeReason == MobileSessionRevokeReasons.Rotated)
            {
                // A token that was already exchanged is being used again. Ending the family's live token is the decision: if a live token
                // was there, the session had not been replaced and this is reuse; assume it leaked and end the whole session.
                int ended = await _context.MobileRefreshTokens.Where(t => t.FamilyId == presented.FamilyId && t.RevokedAtIst == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                        .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.ReuseDetected), cancellationToken);

                if (ended == 0)
                {
                    // Nothing was live: the session had already ended. Nothing has been changed yet, so leave the transaction and read the
                    // family's latest state afresh (this transaction's snapshot may predate a login that has just committed).
                    await transaction.RollbackAsync(cancellationToken);
                    string? familyEnd = await _context.MobileRefreshTokens.AsNoTracking()
                        .Where(t => t.FamilyId == presented.FamilyId)
                        .OrderByDescending(t => t.Id)
                        .Select(t => t.RevokeReason)
                        .FirstOrDefaultAsync(cancellationToken);

                    // Replaced by a newer login: that, not reuse, is what the old device is told. The NewLogin reason is left as it is.
                    if (familyEnd == MobileSessionRevokeReasons.NewLogin)
                        return new RefreshRotationResult(RefreshRotationOutcome.Invalid, presented.ProfileId, presented.FamilyId, MobileSessionRevokeReasons.NewLogin);
                }

                await _context.MobileRefreshTokens.Where(t => t.TokenHash == presentedTokenHash)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReuseDetectedAtIst, (DateTime?)nowUtc), cancellationToken);
                if (ended > 0) await transaction.CommitAsync(cancellationToken);
                return new RefreshRotationResult(RefreshRotationOutcome.ReuseDetected, presented.ProfileId, presented.FamilyId);
            }

            // Revoked for another reason (logout, MPIN change, new login) or simply expired: unusable, but not a theft signal.
            await transaction.RollbackAsync(cancellationToken);
            return new RefreshRotationResult(
                presented.RevokedAtIst == null ? RefreshRotationOutcome.Expired : RefreshRotationOutcome.Invalid,
                presented.ProfileId, presented.FamilyId, presented.RevokeReason);
        });
    }

    public async Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        Guid? family = await _context.MobileRefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(cancellationToken);
        if (family == null) return false;

        await _context.MobileRefreshTokens.Where(t => t.FamilyId == family.Value && t.RevokedAtIst == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken);
        return true;
    }

    public async Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _context.MobileRefreshTokens.Where(t => t.ProfileId == profileId && t.RevokedAtIst == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken);

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default)
        => await (from t in _context.MobileRefreshTokens.AsNoTracking()
                  join p in _context.MobileUserProfiles.AsNoTracking() on t.ProfileId equals p.Id
                  where t.FamilyId == sessionId && t.ProfileId == profileId && t.RevokedAtIst == null && p.IsActive
                  select t.Id).AnyAsync(cancellationToken);

    public async Task<MobileSessionState> GetSessionStateAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _context.MobileUserProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new { p.IsActive, Blocked = p.AccessBlockedAtIst != null })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile == null) return MobileSessionState.Revoked;

        // The newest row of the session decides: an older row of the same session was only rotated, which says nothing about its end.
        var latest = await _context.MobileRefreshTokens.AsNoTracking()
            .Where(t => t.FamilyId == sessionId && t.ProfileId == profileId)
            .OrderByDescending(t => t.Id)
            .Select(t => new { t.RevokedAtIst, t.RevokeReason })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest == null) return MobileSessionState.Revoked;

        // A live token on a blocked profile is the moment between a committed block and the revocation reaching this row: the block marker wins.
        if (latest.RevokedAtIst == null)
            return profile.Blocked ? MobileSessionState.AdminBlocked : profile.IsActive ? MobileSessionState.Active : MobileSessionState.Revoked;

        if (latest.RevokeReason == MobileSessionRevokeReasons.AdminBlocked) return MobileSessionState.AdminBlocked;

        // Replaced by a newer login (latest login wins). A profile that is blocked right now never reports this: SURAKSHA_1001 stays exclusive
        // to the block, and a blocked person with an old replaced session gets the plain revoked answer as before.
        return latest.RevokeReason == MobileSessionRevokeReasons.NewLogin && !profile.Blocked ? MobileSessionState.Replaced : MobileSessionState.Revoked;
    }
}
