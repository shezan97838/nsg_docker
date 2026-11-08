using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class UserPisRepository : IUserPisRepository
{
    private readonly AppDbContext _context;

    public UserPisRepository(AppDbContext context) => _context = context;

    public async Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AsNoTracking().FirstOrDefaultAsync(p => p.NsgNumber == nsgNumber, cancellationToken);

    public async Task<UserPis?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, long assignedTo, CancellationToken cancellationToken = default)
    {
        // The ownership restriction is the FIRST predicate and feeds the total, the page, search and every filter: a record assigned to another
        // admin or to nobody (assigned_to IS NULL) is never counted, matched or returned.
        IQueryable<UserPis> query = _context.UsersPis.AsNoTracking().Where(p => p.AssignedTo == assignedTo);

        if (filter.IsActive.HasValue)
            query = query.Where(p => p.IsActive == filter.IsActive.Value);

        // Lookup values are plain text on the row; the filters are exact (case-insensitive under the default MySQL collation).
        if (!string.IsNullOrWhiteSpace(filter.NsgRank))
        {
            string rank = filter.NsgRank.Trim();
            query = query.Where(p => p.NsgRank == rank);
        }
        if (!string.IsNullOrWhiteSpace(filter.NsgUnit))
        {
            string unit = filter.NsgUnit.Trim();
            query = query.Where(p => p.NsgUnit == unit);
        }
        if (!string.IsNullOrWhiteSpace(filter.ParentDepartmentRank))
        {
            string parent = filter.ParentDepartmentRank.Trim();
            query = query.Where(p => p.ParentDepartmentRank == parent);
        }
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            string category = filter.Category.Trim();
            query = query.Where(p => p.Category == category);
        }        if (filter.MobileAppActivationStatus.HasValue)
            query = query.Where(p => p.MobileAppActivationStatus == filter.MobileAppActivationStatus.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();
            query = query.Where(p => p.NsgNumber.Contains(term) || (p.ServiceNumber != null && p.ServiceNumber.Contains(term))
                || (p.FullName != null && p.FullName.Contains(term))
                || p.MobileNumber.Contains(term));
        }

        int total = await query.CountAsync(cancellationToken);
        List<UserPis> items = await query
            .OrderBy(p => p.NsgNumber).ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken cancellationToken = default)
    {
        await _context.UsersPis.AddAsync(entity, cancellationToken);
        return await TrySaveAsync(cancellationToken);
    }

    // The entity is tracked, so SaveChanges detects what changed. DbSet.Update() is deliberately not used: it would mark
    // every column (including the immutable NSG number) as modified.
    public async Task<PisSaveOutcome> UpdateAsync(UserPis entity, long ownerAdminId, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Ownership guard, first statement of the transaction: matches the row only while it is still assigned to this admin and rewrites the
            // value it already holds, so nothing changes but the row stays locked until commit. A transfer that committed after the record was
            // loaded leaves no match (refused, nothing saved); one still open makes this wait and re-evaluate. Same lock as a transfer takes.
            int locked = await _context.UsersPis.Where(p => p.Id == entity.Id && p.AssignedTo == ownerAdminId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, ownerAdminId), cancellationToken);
            if (locked == 0
                && !await _context.UsersPis.AsNoTracking().AnyAsync(p => p.Id == entity.Id && p.AssignedTo == ownerAdminId, cancellationToken))
                return PisSaveOutcome.NotOwner;

            PisSaveOutcome outcome = await TrySaveAsync(cancellationToken);
            if (outcome == PisSaveOutcome.Saved) await transaction.CommitAsync(cancellationToken);
            return outcome;
        });
    }

    /// <summary>Saves, translating a unique-index violation into an outcome; any other failure propagates.</summary>
    private async Task<PisSaveOutcome> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return PisSaveOutcome.Saved;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql
            && Classify(mysql.Message) != null)
        {
            return Classify(mysql.Message)!.Value;
        }
    }

    // MySQL reports the violated index by name; the duplicated value in the message is never copied anywhere.
    private static PisSaveOutcome? Classify(string message)
    {
        if (message.Contains("ix_users_pis_nsg_number", StringComparison.OrdinalIgnoreCase)) return PisSaveOutcome.DuplicateNsgNumber;
        if (message.Contains("ix_users_pis_mobile_number", StringComparison.OrdinalIgnoreCase)) return PisSaveOutcome.DuplicateMobileNumber;
        return null;
    }
}
