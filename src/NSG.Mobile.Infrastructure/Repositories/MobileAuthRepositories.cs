using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class MobileAuthGrantRepository : IMobileAuthGrantRepository
{
    private readonly AppDbContext _context;

    public MobileAuthGrantRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(MobileAuthGrant grant, CancellationToken cancellationToken = default)
    {
        await _context.MobileAuthGrants.AddAsync(grant, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MobileAuthGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => await _context.MobileAuthGrants.AsNoTracking().FirstOrDefaultAsync(g => g.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryConsumeAsync(long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _context.MobileAuthGrants
            .Where(g => g.Id == grantId && g.ConsumedAtIst == null && g.ExpiresAtIst > nowUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.ConsumedAtIst, (DateTime?)nowUtc), cancellationToken) == 1;

    public async Task InvalidateForNsgNumberAsync(string nsgNumber, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _context.MobileAuthGrants
            .Where(g => g.NsgNumber == nsgNumber && g.ConsumedAtIst == null)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.ConsumedAtIst, (DateTime?)nowUtc), cancellationToken);
}

public class MobileUserProfileRepository : IMobileUserProfileRepository
{
    private readonly AppDbContext _context;

    public MobileUserProfileRepository(AppDbContext context) => _context = context;

    public async Task<MobileUserProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.NsgNumber == nsgNumber, cancellationToken);

    public async Task<MobileUserProfile?> GetByMobileNumberAsync(string mobileNumber, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.MobileNumber == mobileNumber, cancellationToken);

    public async Task<MobileUserProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<FailedLoginState> RegisterFailedLoginAsync(
        long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // Two single statements: the increment is atomic in the database, so parallel wrong guesses cannot undercount.
        await _context.MobileUserProfiles.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedLoginAttempts, p => p.FailedLoginAttempts + 1)
                .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowUtc), cancellationToken);

        DateTime lockedUntil = nowUtc.Add(lockoutDuration);
        await _context.MobileUserProfiles
            .Where(p => p.Id == id && p.FailedLoginAttempts >= maxAttempts && (p.LockedUntilIst == null || p.LockedUntilIst <= nowUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LockedUntilIst, (DateTime?)lockedUntil), cancellationToken);

        var state = await _context.MobileUserProfiles.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.FailedLoginAttempts, p.LockedUntilIst })
            .FirstAsync(cancellationToken);
        return new FailedLoginState(state.FailedLoginAttempts, state.LockedUntilIst);
    }

    public async Task ClearExpiredLockoutAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles
            .Where(p => p.Id == id && p.LockedUntilIst != null && p.LockedUntilIst <= nowUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockedUntilIst, (DateTime?)null), cancellationToken);

    public async Task RecordSuccessfulLoginAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FailedLoginAttempts, 0)
                .SetProperty(p => p.LockedUntilIst, (DateTime?)null)
                .SetProperty(p => p.LastLoginAtIst, (DateTime?)nowUtc), cancellationToken);

    public async Task SetMpinAsync(long id, string mpinHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.MobileUserProfiles.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MpinHash, mpinHash)
                    .SetProperty(p => p.FailedLoginAttempts, 0)
                    .SetProperty(p => p.LockedUntilIst, (DateTime?)null)
                    .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowUtc), cancellationToken);

            // A changed MPIN ends every session: the person signs in again with the new one.
            await _context.MobileRefreshTokens.Where(t => t.ProfileId == id && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.MpinChanged), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<ProfileCreateOutcome> CreateWithGrantAsync(
        MobileUserProfile profile, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // The grant is claimed first: of two concurrent requests with the same grant, only one reaches the insert.
            int claimed = await _context.MobileAuthGrants
                .Where(g => g.Id == grantId && g.ConsumedAtIst == null && g.ExpiresAtIst > nowUtc)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ConsumedAtIst, (DateTime?)nowUtc), cancellationToken);
            if (claimed == 0) return ProfileCreateOutcome.GrantUnavailable;

            _context.MobileUserProfiles.Add(profile);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql)
            {
                _context.Entry(profile).State = EntityState.Detached;
                await transaction.RollbackAsync(cancellationToken);   // gives the grant back: the person may retry after IT fixes the data
                return mysql.Message.Contains("ix_mobile_user_profiles_mobile_number", StringComparison.OrdinalIgnoreCase)
                    ? ProfileCreateOutcome.DuplicateMobileNumber
                    : ProfileCreateOutcome.DuplicateNsgNumber;
            }

            await transaction.CommitAsync(cancellationToken);
            return ProfileCreateOutcome.Created;
        });
    }

    public async Task<bool> ResetMpinWithGrantAsync(
        long profileId, string nsgNumber, string mpinHash, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            int claimed = await _context.MobileAuthGrants
                .Where(g => g.Id == grantId && g.ConsumedAtIst == null && g.ExpiresAtIst > nowUtc)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ConsumedAtIst, (DateTime?)nowUtc), cancellationToken);
            if (claimed == 0) return false;

            await _context.MobileUserProfiles.Where(p => p.Id == profileId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MpinHash, mpinHash)
                    .SetProperty(p => p.FailedLoginAttempts, 0)
                    .SetProperty(p => p.LockedUntilIst, (DateTime?)null)
                    .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowUtc), cancellationToken);

            await _context.MobileRefreshTokens.Where(t => t.ProfileId == profileId && t.RevokedAtIst == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtIst, (DateTime?)nowUtc)
                    .SetProperty(t => t.RevokeReason, MobileSessionRevokeReasons.MpinReset), cancellationToken);

            // Whatever else was outstanding (an older reset, a registration grant) dies with the old MPIN.
            await _context.MobileAuthGrants.Where(g => g.NsgNumber == nsgNumber && g.ConsumedAtIst == null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ConsumedAtIst, (DateTime?)nowUtc), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }
}

public class ApproveMobileUserRepository : IApproveMobileUserRepository
{
    private readonly AppDbContext _context;

    public ApproveMobileUserRepository(AppDbContext context) => _context = context;

    public async Task<ApproveMobileUser?> GetPendingByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => await _context.ApproveMobileUsers.AsNoTracking()
            .FirstOrDefaultAsync(a => a.NsgNumber == nsgNumber && a.ApprovalStatus == MobileAppActivationStatus.Pending, cancellationToken);

    public async Task<ApproveMobileUser?> GetLatestApprovedByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => await _context.ApproveMobileUsers.AsNoTracking()
            .Where(a => a.NsgNumber == nsgNumber && a.ApprovalStatus == MobileAppActivationStatus.Approved)
            .OrderByDescending(a => a.ApprovedAtIst)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PendingRequestOutcome> AddPendingAsync(ApproveMobileUser request, CancellationToken cancellationToken = default)
    {
        _context.ApproveMobileUsers.Add(request);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return PendingRequestOutcome.Created;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql
            && mysql.Message.Contains("ix_approve_mobile_users_pending_nsg_number", StringComparison.OrdinalIgnoreCase))
        {
            // Lost the race against a concurrent request for the same NSG number: the unique index is the guarantee.
            _context.Entry(request).State = EntityState.Detached;
            return PendingRequestOutcome.AlreadyPending;
        }
    }

    public async Task<(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount)> GetPagedAsync(
        long ownerAdminId, int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default)
    {
        // Ownership is resolved here, from the CURRENT users_pis.assigned_to (never a copy on the request). The inner join drops requests whose
        // personnel row is missing, and the equality drops unassigned (NULL) and other admins' rows - before the count, the filters and the page.
        var query = from a in _context.ApproveMobileUsers.AsNoTracking()
                    join p in _context.UsersPis.AsNoTracking() on a.NsgNumber equals p.NsgNumber
                    where p.AssignedTo == ownerAdminId
                    select new { Request = a, FullName = p.FullName };

        if (status.HasValue)
            query = query.Where(x => x.Request.ApprovalStatus == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(x => x.Request.NsgNumber.Contains(term) || (x.FullName != null && x.FullName.Contains(term)));
        }

        int total = await query.CountAsync(cancellationToken);
        var page = await query
            .OrderByDescending(x => x.Request.RequestedAtIst).ThenByDescending(x => x.Request.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (page.Select(x => new ApprovalRequestRow(x.Request, x.FullName)).ToList(), total);
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        long id, MobileAppActivationStatus decision, long adminProfileId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        // approved_by is an int column: refuse (throws) rather than truncate. The service has already refused an id this large.
        int approvedBy = checked((int)adminProfileId);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // 1. Ownership lock - the FIRST statement of the transaction (no earlier read may fix a snapshot). It matches the personnel row of this
            //    request only while assigned_to is still this admin, and rewrites the value it already holds: no data changes, but the row stays
            //    locked until commit/rollback. A transfer that committed first leaves no match (refused); one that is still open makes this wait,
            //    then re-evaluates against the committed value; one that starts later waits for this transaction. Lock order is always
            //    users_pis row, then approve_mobile_users row.
            int locked = await _context.UsersPis
                .Where(p => p.AssignedTo == adminProfileId && _context.ApproveMobileUsers.Any(a => a.Id == id && a.NsgNumber == p.NsgNumber))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, adminProfileId), cancellationToken);

            if (locked == 0)
            {
                // A connection that counts changed rows instead of matched rows reports 0 for the no-op above; the row is still locked if it matched.
                bool owned = await _context.UsersPis.AsNoTracking()
                    .AnyAsync(p => p.AssignedTo == adminProfileId && _context.ApproveMobileUsers.Any(a => a.Id == id && a.NsgNumber == p.NsgNumber), cancellationToken);
                if (!owned)
                {
                    string? nsg = await _context.ApproveMobileUsers.AsNoTracking().Where(a => a.Id == id).Select(a => a.NsgNumber).FirstOrDefaultAsync(cancellationToken);
                    if (nsg == null) return new ApprovalDecisionResult(ApprovalDecisionOutcome.NotFound);

                    bool personnelExists = await _context.UsersPis.AsNoTracking().AnyAsync(p => p.NsgNumber == nsg, cancellationToken);
                    return new ApprovalDecisionResult(personnelExists ? ApprovalDecisionOutcome.NotOwner : ApprovalDecisionOutcome.PersonnelRecordMissing);
                }
            }

            // 2. Claim the request: only a row that is still Pending is updated, so of two simultaneous decisions exactly one wins.
            int claimed = await _context.ApproveMobileUsers
                .Where(a => a.Id == id && a.ApprovalStatus == MobileAppActivationStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.ApprovalStatus, decision)
                    .SetProperty(a => a.ApprovedBy, (int?)approvedBy)
                    .SetProperty(a => a.ApprovedAtIst, (DateTime?)nowIst)
                    .SetProperty(a => a.Remarks, remarks)
                    .SetProperty(a => a.UpdatedAtIst, (DateTime?)nowIst)
                    .SetProperty(a => a.UpdatedBy, (int?)approvedBy), cancellationToken);

            if (claimed == 0)
            {
                MobileAppActivationStatus? current = await _context.ApproveMobileUsers.AsNoTracking()
                    .Where(a => a.Id == id).Select(a => (MobileAppActivationStatus?)a.ApprovalStatus).FirstOrDefaultAsync(cancellationToken);
                return current == null
                    ? new ApprovalDecisionResult(ApprovalDecisionOutcome.NotFound)
                    : new ApprovalDecisionResult(ApprovalDecisionOutcome.AlreadyDecided, CurrentStatus: current);
            }

            ApproveMobileUser request = await _context.ApproveMobileUsers.AsNoTracking().FirstAsync(a => a.Id == id, cancellationToken);

            // 3. Keep users_pis in step with the request, in the same transaction (the row is already locked by step 1).
            int updated = await _context.UsersPis
                .Where(p => p.NsgNumber == request.NsgNumber)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MobileAppActivationStatus, decision)
                    .SetProperty(p => p.UpdatedAtIst, (DateTime?)nowIst)
                    .SetProperty(p => p.UpdatedBy, (int?)approvedBy), cancellationToken);

            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ApprovalDecisionResult(ApprovalDecisionOutcome.PersonnelRecordMissing);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ApprovalDecisionResult(ApprovalDecisionOutcome.Decided, request);
        });
    }
}
