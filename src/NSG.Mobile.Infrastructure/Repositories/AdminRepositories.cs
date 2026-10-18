using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class AdminRoleRepository : IAdminRoleRepository
{
    private readonly AppDbContext _context;

    public AdminRoleRepository(AppDbContext context) => _context = context;

    public async Task<AdminRole?> GetByCodeAsync(string roleCode, CancellationToken cancellationToken = default)
        => await _context.AdminRoles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleCode == roleCode, cancellationToken);

    public async Task<IReadOnlyList<AdminRole>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.AdminRoles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Id).ToListAsync(cancellationToken);
}

public class AdminProfileRepository : IAdminProfileRepository
{
    private readonly AppDbContext _context;

    public AdminProfileRepository(AppDbContext context) => _context = context;

    public async Task<AdminProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.AsNoTracking().Include(p => p.Role).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<AdminProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.AsNoTracking().Include(p => p.Role).FirstOrDefaultAsync(p => p.NsgNumber == nsgNumber, cancellationToken);

    private sealed class Flat
    {
        public AdminProfile Profile { get; init; } = null!;
        public AdminRole Role { get; init; } = null!;
        public string? FullName { get; init; }
    }

    // The role and the PIS name are joined explicitly: Include inside a left-join projection is not translated by EF.
    private IQueryable<Flat> Flats() =>
        from p in _context.AdminProfiles.AsNoTracking()
        join r in _context.AdminRoles.AsNoTracking() on p.RoleId equals r.Id
        join u in _context.UsersPis.AsNoTracking() on p.NsgNumber equals u.NsgNumber into pis
        from u in pis.DefaultIfEmpty()
        select new Flat { Profile = p, Role = r, FullName = u.FullName };

    private static AdminAccountRow ToRow(Flat flat)
    {
        flat.Profile.Role = flat.Role;
        return new AdminAccountRow(flat.Profile, flat.FullName);
    }

    public async Task<AdminAccountRow?> GetRowByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        Flat? flat = await Flats().FirstOrDefaultAsync(f => f.Profile.Id == id, cancellationToken);
        return flat == null ? null : ToRow(flat);
    }

    public async Task<(IReadOnlyList<AdminAccountRow> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, AdminAccountFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<Flat> query = Flats();
        if (!string.IsNullOrWhiteSpace(filter.RoleCode)) query = query.Where(f => f.Role.RoleCode == filter.RoleCode);
        if (filter.ActivationStatus.HasValue) query = query.Where(f => f.Profile.ActivationStatus == filter.ActivationStatus.Value);
        if (filter.IsActive.HasValue) query = query.Where(f => f.Profile.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();
            query = query.Where(f => f.Profile.NsgNumber.Contains(term) || (f.FullName != null && f.FullName.Contains(term)));
        }

        int total = await query.CountAsync(cancellationToken);
        List<Flat> page = await query.OrderBy(f => f.Profile.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (page.Select(ToRow).ToList(), total);
    }

    public async Task<AdminCreateOutcome> AddAsync(AdminProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.AdminProfiles.AddAsync(profile, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return AdminCreateOutcome.Created;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            _context.Entry(profile).State = EntityState.Detached;
            return AdminCreateOutcome.DuplicateNsgNumber;
        }
    }

    public async Task<bool> AnySuperAdminAsync(CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.AsNoTracking().AnyAsync(p => p.Role!.RoleCode == AdminRoleCodes.SuperAdmin, cancellationToken);

    public async Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.AsNoTracking().CountAsync(
            p => p.Role!.RoleCode == AdminRoleCodes.SuperAdmin && p.IsActive && p.ActivationStatus == AdminActivationStatus.Active, cancellationToken);

    public async Task<bool> ActivateAsync(long id, string passwordHash, DateTime nowIst, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles
            .Where(p => p.Id == id && p.IsActive && p.ActivationStatus == AdminActivationStatus.PendingActivation && p.PasswordHash == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PasswordHash, passwordHash)
                .SetProperty(p => p.ActivationStatus, AdminActivationStatus.Active)
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockoutEndIst, (DateTime?)null)
                .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowIst)
                .SetProperty(p => p.UpdatedBy, (long?)id), cancellationToken) == 1;

    public async Task<AdminStateChangeOutcome> SetEnabledAsync(long id, bool enabled, long actorId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        int changed = await _context.AdminProfiles.Where(p => p.Id == id && p.IsActive != enabled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, enabled)
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockoutEndIst, (DateTime?)null)
                .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowIst)
                .SetProperty(p => p.UpdatedBy, (long?)actorId), cancellationToken);
        if (changed == 1) return AdminStateChangeOutcome.Changed;

        return await _context.AdminProfiles.AsNoTracking().AnyAsync(p => p.Id == id, cancellationToken)
            ? AdminStateChangeOutcome.AlreadyInTargetState
            : AdminStateChangeOutcome.NotFound;
    }

    public async Task<FailedLoginState> RegisterFailedLoginAsync(
        long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        // Two single statements: the increment is atomic in the database, so parallel wrong guesses cannot undercount.
        await _context.AdminProfiles.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FailedLoginAttempts, p => p.FailedLoginAttempts + 1), cancellationToken);

        DateTime lockedUntil = nowIst.Add(lockoutDuration);
        await _context.AdminProfiles
            .Where(p => p.Id == id && p.FailedLoginAttempts >= maxAttempts && (p.LockoutEndIst == null || p.LockoutEndIst <= nowIst))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LockoutEndIst, (DateTime?)lockedUntil), cancellationToken);

        var state = await _context.AdminProfiles.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.FailedLoginAttempts, p.LockoutEndIst }).FirstAsync(cancellationToken);
        return new FailedLoginState(state.FailedLoginAttempts, state.LockoutEndIst);
    }

    public async Task ClearExpiredLockoutAsync(long id, DateTime nowIst, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.Where(p => p.Id == id && p.LockoutEndIst != null && p.LockoutEndIst <= nowIst)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockoutEndIst, (DateTime?)null), cancellationToken);

    public async Task RecordSuccessfulLoginAsync(long id, DateTime nowIst, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockoutEndIst, (DateTime?)null)
                .SetProperty(p => p.LastLoginAtIst, (DateTime?)nowIst), cancellationToken);
}

public class AdminRefreshTokenRepository : IAdminRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public AdminRefreshTokenRepository(AppDbContext context) => _context = context;

    public async Task StartSessionAsync(AdminRefreshToken first, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.AdminRefreshTokens
                .Where(t => t.AdminProfileId == first.AdminProfileId && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                    .SetProperty(t => t.RevokeReason, AdminSessionRevokeReasons.NewLogin), cancellationToken);

            _context.AdminRefreshTokens.Add(first);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, AdminRefreshToken next, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Claim the presented token. Of any number of simultaneous requests carrying the same token, exactly one updates a row.
            int claimed = await _context.AdminRefreshTokens
                .Where(t => t.TokenHash == presentedTokenHash && t.RevokedAtIst == null && t.ExpiresAtIst > nowIst)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                    .SetProperty(t => t.RevokeReason, AdminSessionRevokeReasons.Rotated), cancellationToken);

            var presented = await _context.AdminRefreshTokens.AsNoTracking()
                .Where(t => t.TokenHash == presentedTokenHash)
                .Select(t => new { t.AdminProfileId, t.FamilyId, t.RevokedAtIst, t.RevokeReason })
                .FirstOrDefaultAsync(cancellationToken);

            if (presented == null) return new RefreshRotationResult(RefreshRotationOutcome.Invalid);

            if (claimed == 1)
            {
                next.AdminProfileId = presented.AdminProfileId;
                next.FamilyId = presented.FamilyId;
                _context.AdminRefreshTokens.Add(next);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new RefreshRotationResult(RefreshRotationOutcome.Rotated, presented.AdminProfileId, presented.FamilyId);
            }

            if (presented.RevokedAtIst != null && presented.RevokeReason == AdminSessionRevokeReasons.Rotated)
            {
                // A token that was already exchanged is being used again: assume it leaked and end the whole session.
                await _context.AdminRefreshTokens.Where(t => t.TokenHash == presentedTokenHash)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReuseDetectedAtIst, (DateTime?)nowIst), cancellationToken);
                await _context.AdminRefreshTokens.Where(t => t.FamilyId == presented.FamilyId && t.RevokedAtIst == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                        .SetProperty(t => t.RevokeReason, AdminSessionRevokeReasons.ReuseDetected), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new RefreshRotationResult(RefreshRotationOutcome.ReuseDetected, presented.AdminProfileId, presented.FamilyId);
            }

            // Revoked for another reason (logout, new login, deactivation) or simply expired: unusable, but not a theft signal.
            await transaction.RollbackAsync(cancellationToken);
            return new RefreshRotationResult(
                presented.RevokedAtIst == null ? RefreshRotationOutcome.Expired : RefreshRotationOutcome.Invalid,
                presented.AdminProfileId, presented.FamilyId);
        });
    }

    public async Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Guid? family = await _context.AdminRefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(cancellationToken);
        if (family == null) return false;

        await _context.AdminRefreshTokens.Where(t => t.FamilyId == family.Value && t.RevokedAtIst == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken);
        return true;
    }

    public async Task RevokeAllForProfileAsync(long adminProfileId, string reason, DateTime nowIst, CancellationToken cancellationToken = default)
        => await _context.AdminRefreshTokens.Where(t => t.AdminProfileId == adminProfileId && t.RevokedAtIst == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtIst, (DateTime?)nowIst)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken);

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, long adminProfileId, string roleCode, CancellationToken cancellationToken = default)
        => await (from t in _context.AdminRefreshTokens.AsNoTracking()
                  join p in _context.AdminProfiles.AsNoTracking() on t.AdminProfileId equals p.Id
                  join r in _context.AdminRoles.AsNoTracking() on p.RoleId equals r.Id
                  where t.FamilyId == sessionId && t.AdminProfileId == adminProfileId && t.RevokedAtIst == null
                        && p.IsActive && p.ActivationStatus == AdminActivationStatus.Active && r.IsActive && r.RoleCode == roleCode
                  select t.Id).AnyAsync(cancellationToken);
}
