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
        => await _context.UsersPis.AsNoTracking()
            .Include(p => p.NsgRank)
            .Include(p => p.NsgTrade)
            .Include(p => p.ParentDepartmentRank)
            .Include(p => p.PdTrade)
            .Include(p => p.NsgUnit)
            .Include(p => p.SideSteppedFromNsgUnit)
            .Include(p => p.Education)
            .Include(p => p.Religion)
            .Include(p => p.PermanentState)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<UserPis> query = _context.UsersPis.AsNoTracking();

        if (filter.IsActive.HasValue)
            query = query.Where(p => p.IsActive == filter.IsActive.Value);
        if (filter.NsgRankId.HasValue)
            query = query.Where(p => p.NsgRankId == filter.NsgRankId.Value);
        if (filter.NsgUnitId.HasValue)
            query = query.Where(p => p.NsgUnitId == filter.NsgUnitId.Value);
        if (filter.ParentDepartmentRankId.HasValue)
            query = query.Where(p => p.ParentDepartmentRankId == filter.ParentDepartmentRankId.Value);
        if (filter.Category.HasValue)
            query = query.Where(p => p.Category == filter.Category.Value);
        if (filter.MobileAppActivationStatus.HasValue)
            query = query.Where(p => p.MobileAppActivationStatus == filter.MobileAppActivationStatus.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();
            query = query.Where(p => p.NsgNumber.Contains(term) || p.ServiceNumber.Contains(term)
                || (p.FullName != null && p.FullName.Contains(term))
                || (p.MobileNumber != null && p.MobileNumber.Contains(term)));
        }

        int total = await query.CountAsync(cancellationToken);
        List<UserPis> items = await query
            .Include(p => p.NsgRank)
            .Include(p => p.NsgTrade)
            .Include(p => p.ParentDepartmentRank)
            .Include(p => p.PdTrade)
            .Include(p => p.NsgUnit)
            .Include(p => p.SideSteppedFromNsgUnit)
            .Include(p => p.Education)
            .Include(p => p.Religion)
            .Include(p => p.PermanentState)
            .OrderBy(p => p.NsgNumber).ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<string>> GetMissingReferencesAsync(UserPis c, CancellationToken cancellationToken = default)
    {
        List<string> missing = new();

        if (c.NsgRankId.HasValue && !await _context.RankHierarchies.AnyAsync(x => x.Id == c.NsgRankId.Value, cancellationToken))
            missing.Add(nameof(UserPis.NsgRankId));
        if (c.ParentDepartmentRankId.HasValue && !await _context.ParentDepartmentRanks.AnyAsync(x => x.Id == c.ParentDepartmentRankId.Value, cancellationToken))
            missing.Add(nameof(UserPis.ParentDepartmentRankId));
        if (c.NsgTradeId.HasValue && !await _context.NsgTrades.AnyAsync(x => x.Id == c.NsgTradeId.Value, cancellationToken))
            missing.Add(nameof(UserPis.NsgTradeId));
        if (c.PdTradeId.HasValue && !await _context.PdTrades.AnyAsync(x => x.Id == c.PdTradeId.Value, cancellationToken))
            missing.Add(nameof(UserPis.PdTradeId));
        if (c.NsgUnitId.HasValue && !await _context.NsgUnits.AnyAsync(x => x.Id == c.NsgUnitId.Value, cancellationToken))
            missing.Add(nameof(UserPis.NsgUnitId));
        if (c.SideSteppedFromNsgUnitId.HasValue && !await _context.NsgUnits.AnyAsync(x => x.Id == c.SideSteppedFromNsgUnitId.Value, cancellationToken))
            missing.Add(nameof(UserPis.SideSteppedFromNsgUnitId));
        if (c.EducationId.HasValue && !await _context.EducationQualifications.AnyAsync(x => x.Id == c.EducationId.Value, cancellationToken))
            missing.Add(nameof(UserPis.EducationId));
        if (c.ReligionId.HasValue && !await _context.Religions.AnyAsync(x => x.Id == c.ReligionId.Value, cancellationToken))
            missing.Add(nameof(UserPis.ReligionId));
        if (c.PermanentStateId.HasValue && !await _context.States.AnyAsync(x => x.Id == c.PermanentStateId.Value, cancellationToken))
            missing.Add(nameof(UserPis.PermanentStateId));

        return missing;
    }

    public async Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken cancellationToken = default)
    {
        await _context.UsersPis.AddAsync(entity, cancellationToken);
        return await TrySaveAsync(cancellationToken);
    }

    // The entity is tracked, so SaveChanges detects what changed. DbSet.Update() is deliberately not used: it would mark
    // every column (including the immutable NSG number) as modified.
    public async Task<PisSaveOutcome> UpdateAsync(UserPis entity, CancellationToken cancellationToken = default)
        => await TrySaveAsync(cancellationToken);

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
        if (message.Contains("ix_users_pis_pan_number_hash", StringComparison.OrdinalIgnoreCase)) return PisSaveOutcome.DuplicatePanNumber;
        if (message.Contains("ix_users_pis_salary_account_number_hash", StringComparison.OrdinalIgnoreCase)) return PisSaveOutcome.DuplicateSalaryAccountNumber;
        return null;
    }
}
