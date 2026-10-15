using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class EducationQualificationRepository : IEducationQualificationRepository
{
    private readonly AppDbContext _context;

    public EducationQualificationRepository(AppDbContext context) => _context = context;

    public async Task<EducationQualification?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.EducationQualifications.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<EducationQualification> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        IQueryable<EducationQualification> query = _context.EducationQualifications.AsNoTracking();

        if (isActive.HasValue)
            query = query.Where(e => e.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(e => e.CourseName.Contains(term));
        }

        int total = await query.CountAsync(cancellationToken);
        List<EducationQualification> items = await query
            .OrderBy(e => e.CourseName).ThenBy(e => e.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<bool> NameExistsAsync(string courseName, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.EducationQualifications.AnyAsync(e => e.CourseName == courseName && (!excludeId.HasValue || e.Id != excludeId.Value), cancellationToken);

    public async Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AnyAsync(u => u.EducationId == id, cancellationToken);

    public async Task<bool> AddAsync(EducationQualification entity, CancellationToken cancellationToken = default)
    {
        await _context.EducationQualifications.AddAsync(entity, cancellationToken);
        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(EducationQualification entity, CancellationToken cancellationToken = default)
        => await TrySaveAsync(cancellationToken);   // tracked entity: SaveChanges detects what changed

    /// <summary>Saves, translating a duplicate-key violation (a race past the pre-check) into <c>false</c>; anything else propagates.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            return false;
        }
    }
}
