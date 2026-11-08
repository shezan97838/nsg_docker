using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>
/// PIS Admin block / restore of mobile access. Follows the pattern of <see cref="MobileUserProfileRepository"/>: conditional single
/// statements, and one transaction where two tables change together. No raw SQL.
/// </summary>
public class MobileAccessRepository : IMobileAccessRepository
{
    private readonly AppDbContext _context;

    public MobileAccessRepository(AppDbContext context) => _context = context;

    public async Task<MobileAccessChangeOutcome> BlockAsync(long profileId, int adminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // The conditional update is the claim: of any number of simultaneous block requests exactly one changes the row. It also takes
            // the profile's row lock, which a concurrent login (see StartSessionAsync) waits on.
            int changed = await _context.MobileUserProfiles
                .Where(p => p.Id == profileId && p.IsActive && p.AccessBlockedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.IsActive, false)
                    .SetProperty(p => p.AccessBlockedAtIst, (DateTime?)nowIst)
                    .SetProperty(p => p.AccessBlockedBy, (int?)adminId)
                    .SetProperty(p => p.AccessBlockReason, reason)
                    .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowIst), cancellationToken);

            MobileAccessChangeOutcome outcome = MobileAccessChangeOutcome.Changed;
            if (changed == 0)
            {
                var state = await _context.MobileUserProfiles.AsNoTracking()
                    .Where(p => p.Id == profileId)
                    .Select(p => new { Blocked = p.AccessBlockedAtIst != null })
                    .FirstOrDefaultAsync(cancellationToken);

                if (state == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return MobileAccessChangeOutcome.NotFound;
                }

                if (!state.Blocked)
                {
                    // Inactive for a reason unrelated to this mechanism. It is not turned into an admin block, because restore would
                    // then reactivate an account that was disabled for another reason.
                    await transaction.RollbackAsync(cancellationToken);
                    return MobileAccessChangeOutcome.Refused;
                }

                outcome = MobileAccessChangeOutcome.NoChange;
            }

            // Every live session of the profile ends, whatever happened above: a repeated block also clears a stray live row.
            await _context.MobileRefreshTokens
                .Where(t => t.ProfileId == profileId && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.AdminBlocked), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return outcome;
        });
    }

    public async Task<MobileAccessChangeOutcome> RestoreAsync(long profileId, int adminId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        // One statement, conditional on the block marker: only a profile blocked through this mechanism is restored. Refresh tokens are not
        // touched, so every session that was revoked stays revoked and the person has to log in again.
        int changed = await _context.MobileUserProfiles
            .Where(p => p.Id == profileId && p.AccessBlockedAtIst != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, true)
                .SetProperty(p => p.AccessBlockedAtIst, (DateTime?)null)
                .SetProperty(p => p.AccessBlockedBy, (int?)null)
                .SetProperty(p => p.AccessBlockReason, (string?)null)
                .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowIst), cancellationToken);
        if (changed == 1) return MobileAccessChangeOutcome.Changed;

        var state = await _context.MobileUserProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new { p.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (state == null) return MobileAccessChangeOutcome.NotFound;
        return state.IsActive ? MobileAccessChangeOutcome.NoChange : MobileAccessChangeOutcome.Refused;
    }

    public async Task<bool> HasLiveSessionAsync(long profileId, DateTime nowIst, CancellationToken cancellationToken = default)
        => await _context.MobileRefreshTokens.AsNoTracking()
            .AnyAsync(t => t.ProfileId == profileId && t.RevokedAtIst == null && t.ExpiresAtIst > nowIst, cancellationToken);
}
