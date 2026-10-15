using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class MobileRefreshTokenRepository : IMobileRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public MobileRefreshTokenRepository(AppDbContext context) => _context = context;

    public async Task StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.MobileRefreshTokens
                .Where(t => t.ProfileId == first.ProfileId && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.NewLogin), cancellationToken);

            _context.MobileRefreshTokens.Add(first);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
                // A token that was already exchanged is being used again: assume it leaked and end the whole session.
                await _context.MobileRefreshTokens.Where(t => t.TokenHash == presentedTokenHash)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReuseDetectedAtIst, (DateTime?)nowUtc), cancellationToken);
                await _context.MobileRefreshTokens.Where(t => t.FamilyId == presented.FamilyId && t.RevokedAtIst == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                        .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.ReuseDetected), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new RefreshRotationResult(RefreshRotationOutcome.ReuseDetected, presented.ProfileId, presented.FamilyId);
            }

            // Revoked for another reason (logout, MPIN change, new login) or simply expired: unusable, but not a theft signal.
            await transaction.RollbackAsync(cancellationToken);
            return new RefreshRotationResult(
                presented.RevokedAtIst == null ? RefreshRotationOutcome.Expired : RefreshRotationOutcome.Invalid,
                presented.ProfileId, presented.FamilyId);
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
}
