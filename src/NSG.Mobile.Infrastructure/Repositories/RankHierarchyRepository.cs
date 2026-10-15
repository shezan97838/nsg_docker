using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class RankHierarchyRepository : IRankHierarchyRepository
{
    private readonly AppDbContext _context;

    public RankHierarchyRepository(AppDbContext context) => _context = context;

    public async Task<RankHierarchy?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.RankHierarchies.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<RankHierarchy> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        IQueryable<RankHierarchy> query = _context.RankHierarchies.AsNoTracking();

        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(r => r.RankName.Contains(term) || (r.ShortCode != null && r.ShortCode.Contains(term)));
        }

        int total = await query.CountAsync(cancellationToken);
        List<RankHierarchy> items = await query
            .OrderBy(r => r.SrNo)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<RankHierarchy>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await _context.RankHierarchies.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SrNo)
            .ToListAsync(cancellationToken);

    public async Task<bool> SrNoExistsAsync(int srNo, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.RankHierarchies.AnyAsync(r => r.SrNo == srNo && (!excludeId.HasValue || r.Id != excludeId.Value), cancellationToken);

    public async Task<bool> RankNameExistsAsync(string rankName, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.RankHierarchies.AnyAsync(r => r.RankName == rankName && (!excludeId.HasValue || r.Id != excludeId.Value), cancellationToken);

    public async Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AnyAsync(u => u.NsgRankId == id, cancellationToken);

    public async Task AddAsync(RankHierarchy rank, CancellationToken cancellationToken = default)
    {
        await _context.RankHierarchies.AddAsync(rank, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RankHierarchy rank, CancellationToken cancellationToken = default)
    {
        _context.RankHierarchies.Update(rank);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
