using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IGrievanceRepository"/>. Transactions run inside the execution strategy (retry on transient failure is enabled).</summary>
public class GrievanceRepository : IGrievanceRepository
{
    private const string TicketPrefix = "GRV-";

    private readonly AppDbContext _context;

    public GrievanceRepository(AppDbContext context) => _context = context;

    // ── Categories ──

    public async Task<IReadOnlyList<GrievanceCategory>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default)
        => await _context.GrievanceCategories.AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.GrievanceName)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<GrievanceCategory> Items, int TotalCount)> GetCategoriesPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        IQueryable<GrievanceCategory> query = _context.GrievanceCategories.AsNoTracking().Where(c => !c.IsDeleted);
        if (isActive.HasValue) query = query.Where(c => c.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(c => c.GrievanceName.Contains(search));

        int total = await query.CountAsync(cancellationToken);
        List<GrievanceCategory> items = await query
            .OrderBy(c => c.GrievanceName)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<GrievanceCategory?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.GrievanceCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CategoryWriteOutcome> AddCategoryAsync(GrievanceCategory category, CancellationToken cancellationToken = default)
    {
        _context.GrievanceCategories.Add(category);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return CategoryWriteOutcome.Saved;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _context.Entry(category).State = EntityState.Detached;
            return CategoryWriteOutcome.DuplicateName;
        }
    }

    public async Task<CategoryWriteOutcome> UpdateCategoryAsync(
        int id, string name, int? departmentId, bool isActive, long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        GrievanceCategory? category = await _context.GrievanceCategories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
        if (category == null) return CategoryWriteOutcome.NotFound;

        category.GrievanceName = name;
        category.DepartmentId = departmentId;
        category.IsActive = isActive;
        category.UpdatedBy = updatedBy;
        category.UpdatedAtIst = nowIst;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return CategoryWriteOutcome.Saved;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _context.Entry(category).State = EntityState.Detached;
            return CategoryWriteOutcome.DuplicateName;
        }
    }

    public async Task<CategoryDeleteOutcome> SoftDeleteCategoryAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        // Single conditional statement: of two simultaneous requests only one changes the row. The row and its grievances stay.
        int changed = await _context.GrievanceCategories
            .Where(c => c.Id == id && !c.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsDeleted, true)
                .SetProperty(c => c.IsActive, false)
                .SetProperty(c => c.UpdatedBy, deletedBy)
                .SetProperty(c => c.UpdatedAtIst, (DateTime?)nowIst), cancellationToken);
        if (changed == 1) return CategoryDeleteOutcome.Deleted;

        bool exists = await _context.GrievanceCategories.AnyAsync(c => c.Id == id, cancellationToken);
        return exists ? CategoryDeleteOutcome.AlreadyDeleted : CategoryDeleteOutcome.NotFound;
    }

    // ── Grievances ──

    public async Task<GrievanceCreateResult> CreateAsync(
        Grievance grievance, long createdByProfileId, int maxTicketAttempts, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxTicketAttempts; attempt++)
        {
            bool created = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Idempotent for the execution strategy's own retries: start from a clean, untracked grievance each time.
                if (_context.Entry(grievance).State != EntityState.Detached) _context.Entry(grievance).State = EntityState.Detached;
                grievance.Id = 0;

                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                string prefix = $"{TicketPrefix}{grievance.CreatedAtIst.Year:D4}-";
                string? last = await _context.Grievances.AsNoTracking()
                    .Where(g => g.TicketNumber.StartsWith(prefix))
                    .OrderByDescending(g => g.TicketNumber.Length).ThenByDescending(g => g.TicketNumber)
                    .Select(g => g.TicketNumber)
                    .FirstOrDefaultAsync(cancellationToken);
                int next = last != null && int.TryParse(last[prefix.Length..], out int lastNumber) ? lastNumber + 1 : 1;
                grievance.TicketNumber = prefix + next.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

                _context.Grievances.Add(grievance);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    // A concurrent request took the same number; the caller retries with a fresh read.
                    _context.Entry(grievance).State = EntityState.Detached;
                    return false;
                }

                _context.GrievanceStatusLogs.Add(new GrievanceStatusLog
                {
                    GrievancePk = grievance.Id,
                    FromStatus = null,
                    LogAction = GrievanceLogAction.StatusChange,
                    ToStatus = GrievanceStatus.Submitted,
                    Remarks = null,
                    ChangedBy = createdByProfileId,
                    ChangedByType = GrievanceActorType.Employee,
                    ChangedAtIst = grievance.CreatedAtIst
                });
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            });

            if (created) return new GrievanceCreateResult(GrievanceCreateOutcome.Created, grievance);
        }

        return new GrievanceCreateResult(GrievanceCreateOutcome.TicketNumberUnavailable);
    }

    public async Task<(IReadOnlyList<Grievance> Items, int TotalCount)> GetPagedAsync(
        GrievanceListFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<Grievance> query = _context.Grievances.AsNoTracking().Where(g => g.IsActive);
        if (filter.CreatedBy.HasValue) query = query.Where(g => g.CreatedBy == filter.CreatedBy.Value);
        if (filter.AssignedTo.HasValue) query = query.Where(g => g.AssignedTo == filter.AssignedTo.Value);
        if (filter.Status.HasValue) query = query.Where(g => g.CurrentStatus == filter.Status.Value);
        if (filter.CategoryId.HasValue) query = query.Where(g => g.CategoryId == filter.CategoryId.Value);
        if (filter.FromIst.HasValue) query = query.Where(g => g.CreatedAtIst >= filter.FromIst.Value);
        if (filter.ToIstExclusive.HasValue) query = query.Where(g => g.CreatedAtIst < filter.ToIstExclusive.Value);
        if (!string.IsNullOrWhiteSpace(filter.TicketSearch)) query = query.Where(g => g.TicketNumber.Contains(filter.TicketSearch));

        int total = await query.CountAsync(cancellationToken);
        List<Grievance> items = await query
            .Include(g => g.Category)
            .OrderByDescending(g => g.CreatedAtIst).ThenByDescending(g => g.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<Grievance?> GetByIdAsync(long id, long? createdBy, CancellationToken cancellationToken = default)
        => await _context.Grievances.AsNoTracking()
            .Include(g => g.Category)
            .FirstOrDefaultAsync(g => g.Id == id && g.IsActive && (createdBy == null || g.CreatedBy == createdBy), cancellationToken);

    public async Task<IReadOnlyList<GrievanceStatusLog>> GetTimelineAsync(long grievancePk, CancellationToken cancellationToken = default)
        => await _context.GrievanceStatusLogs.AsNoTracking()
            .Where(l => l.GrievancePk == grievancePk)
            .OrderBy(l => l.ChangedAtIst).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

    public async Task<GrievanceStatusChangeResult> ChangeStatusAsync(
        long id, GrievanceStatus from, GrievanceStatus to, long adminProfileId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            IQueryable<Grievance> target = _context.Grievances.Where(g => g.Id == id && g.IsActive && g.CurrentStatus == from);
            int updated = to switch
            {
                GrievanceStatus.ActionTaken => await target.ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.CurrentStatus, to)
                    .SetProperty(g => g.UpdatedAtIst, (DateTime?)nowIst)
                    .SetProperty(g => g.ActionTakenBy, (long?)adminProfileId)
                    .SetProperty(g => g.ActionTakenAtIst, (DateTime?)nowIst)
                    .SetProperty(g => g.ActionRemarks, remarks), cancellationToken),
                GrievanceStatus.Closed => await target.ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.CurrentStatus, to)
                    .SetProperty(g => g.UpdatedAtIst, (DateTime?)nowIst)
                    .SetProperty(g => g.ClosedBy, (long?)adminProfileId)
                    .SetProperty(g => g.ClosedAtIst, (DateTime?)nowIst)
                    .SetProperty(g => g.ClosedRemarks, remarks), cancellationToken),
                _ => await target.ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.CurrentStatus, to)
                    .SetProperty(g => g.UpdatedAtIst, (DateTime?)nowIst), cancellationToken)
            };

            if (updated == 0)
            {
                GrievanceStatus? current = await _context.Grievances.AsNoTracking()
                    .Where(g => g.Id == id && g.IsActive)
                    .Select(g => (GrievanceStatus?)g.CurrentStatus)
                    .FirstOrDefaultAsync(cancellationToken);
                return current == null
                    ? new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.NotFound)
                    : new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.StatusChanged, current);
            }

            _context.GrievanceStatusLogs.Add(new GrievanceStatusLog
            {
                GrievancePk = id,
                LogAction = GrievanceLogAction.StatusChange,
                FromStatus = from,
                ToStatus = to,
                Remarks = remarks,
                ChangedBy = adminProfileId,
                ChangedByType = GrievanceActorType.Admin,
                ChangedAtIst = nowIst
            });
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.Changed);
        });
    }

    public async Task<GrievanceCommentOutcome> AddCommentAsync(
        long id, long adminProfileId, string comment, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            GrievanceStatus? status = await _context.Grievances.AsNoTracking()
                .Where(g => g.Id == id && g.IsActive)
                .Select(g => (GrievanceStatus?)g.CurrentStatus)
                .FirstOrDefaultAsync(cancellationToken);
            if (status == null) return GrievanceCommentOutcome.NotFound;
            if (status == GrievanceStatus.Closed) return GrievanceCommentOutcome.Closed;

            _context.GrievanceStatusLogs.Add(new GrievanceStatusLog
            {
                GrievancePk = id,
                LogAction = GrievanceLogAction.Comment,
                FromStatus = status,
                ToStatus = status.Value,
                Remarks = comment,
                ChangedBy = adminProfileId,
                ChangedByType = GrievanceActorType.Admin,
                ChangedAtIst = nowIst
            });
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return GrievanceCommentOutcome.Added;
        });
    }

    public async Task<GrievanceForwardOutcome> ForwardAsync(
        long id, long adminProfileId, long toAdminId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // One conditional statement: the move happens only while the grievance is open and not already with the target.
            GrievanceStatus? status = await _context.Grievances.AsNoTracking()
                .Where(g => g.Id == id && g.IsActive)
                .Select(g => (GrievanceStatus?)g.CurrentStatus)
                .FirstOrDefaultAsync(cancellationToken);
            if (status == null) return GrievanceForwardOutcome.NotFound;
            if (status == GrievanceStatus.Closed) return GrievanceForwardOutcome.Closed;

            int updated = await _context.Grievances
                .Where(g => g.Id == id && g.IsActive && g.CurrentStatus != GrievanceStatus.Closed
                            && (g.AssignedTo == null || g.AssignedTo != toAdminId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.AssignedTo, (long?)toAdminId)
                    .SetProperty(g => g.UpdatedAtIst, (DateTime?)nowIst), cancellationToken);
            if (updated == 0)
            {
                bool stillOpen = await _context.Grievances.AsNoTracking()
                    .AnyAsync(g => g.Id == id && g.IsActive && g.CurrentStatus != GrievanceStatus.Closed, cancellationToken);
                return stillOpen ? GrievanceForwardOutcome.AlreadyAssigned : GrievanceForwardOutcome.Closed;
            }

            _context.GrievanceStatusLogs.Add(new GrievanceStatusLog
            {
                GrievancePk = id,
                LogAction = GrievanceLogAction.Forward,
                FromStatus = status,
                ToStatus = status.Value,
                Remarks = remarks,
                ForwardedTo = toAdminId,
                ChangedBy = adminProfileId,
                ChangedByType = GrievanceActorType.Admin,
                ChangedAtIst = nowIst
            });
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return GrievanceForwardOutcome.Forwarded;
        });
    }

    // ── Dashboard and lookups ──

    public async Task<GrievanceDashboardData> GetDashboardAsync(DateTime? fromIst, DateTime? toIstExclusive, CancellationToken cancellationToken = default)
    {
        IQueryable<Grievance> query = _context.Grievances.AsNoTracking().Where(g => g.IsActive);
        if (fromIst.HasValue) query = query.Where(g => g.CreatedAtIst >= fromIst.Value);
        if (toIstExclusive.HasValue) query = query.Where(g => g.CreatedAtIst < toIstExclusive.Value);

        var byStatus = await query.GroupBy(g => g.CurrentStatus)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var byCategory = await query.GroupBy(g => new { g.CategoryId, g.Category!.GrievanceName })
            .Select(x => new { x.Key.CategoryId, Name = x.Key.GrievanceName, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var byMonth = await query.GroupBy(g => new { g.CreatedAtIst.Year, g.CreatedAtIst.Month })
            .Select(x => new { x.Key.Year, x.Key.Month, Count = x.Count() })
            .ToListAsync(cancellationToken);

        // Creation to closing, in seconds, over closed grievances only.
        double? averageSeconds = await query
            .Where(g => g.CurrentStatus == GrievanceStatus.Closed && g.ClosedAtIst != null)
            .AverageAsync(g => (double?)EF.Functions.DateDiffSecond(g.CreatedAtIst, g.ClosedAtIst!.Value), cancellationToken);

        return new GrievanceDashboardData(
            byStatus.Sum(x => x.Count),
            byStatus.OrderBy(x => x.Status).Select(x => new GrievanceStatusCount(x.Status, x.Count)).ToList(),
            byCategory.OrderByDescending(x => x.Count).ThenBy(x => x.Name).Select(x => new GrievanceCategoryCount(x.CategoryId, x.Name, x.Count)).ToList(),
            byMonth.OrderBy(x => x.Year).ThenBy(x => x.Month).Select(x => new GrievanceMonthCount(x.Year, x.Month, x.Count)).ToList(),
            averageSeconds);
    }

    public async Task<IReadOnlyDictionary<long, GrievanceEmployeeInfo>> GetEmployeeInfoAsync(IReadOnlyCollection<long> profileIds, CancellationToken cancellationToken = default)
    {
        if (profileIds.Count == 0) return new Dictionary<long, GrievanceEmployeeInfo>();

        // Left joins: a profile whose personnel record or unit is missing still yields its NSG number.
        var rows = await (
            from profile in _context.MobileUserProfiles.AsNoTracking()
            where profileIds.Contains(profile.Id)
            join pis in _context.UsersPis.AsNoTracking() on profile.NsgNumber equals pis.NsgNumber into pisJoin
            from pis in pisJoin.DefaultIfEmpty()
            select new { profile.Id, profile.NsgNumber, FullName = pis != null ? pis.FullName : null, UnitName = pis != null && pis.NsgUnit != null ? pis.NsgUnit.Name : null }
        ).ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => new GrievanceEmployeeInfo(r.Id, r.NsgNumber, r.FullName, r.UnitName));
    }

    public async Task<bool> IsActiveGrievanceAdminAsync(long adminProfileId, CancellationToken cancellationToken = default)
        => await _context.AdminProfiles.AsNoTracking()
            .AnyAsync(p => p.Id == adminProfileId
                           && p.IsActive
                           && p.ActivationStatus == AdminActivationStatus.Active
                           && p.Role!.IsActive
                           && p.Role.RoleCode == AdminRoleCodes.GrievanceAdmin, cancellationToken);

    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
