using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class NsgTradeRepository : INsgTradeRepository
{
    private readonly AppDbContext _context;

    public NsgTradeRepository(AppDbContext context) => _context = context;

    public async Task<NsgTrade?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.NsgTrades.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<NsgTrade> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        IQueryable<NsgTrade> query = _context.NsgTrades.AsNoTracking();

        if (isActive.HasValue)
            query = query.Where(t => t.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(t => t.NsgTradeName.Contains(term));
        }

        int total = await query.CountAsync(cancellationToken);
        List<NsgTrade> items = await query
            .OrderBy(t => t.NsgTradeName).ThenBy(t => t.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.NsgTrades.AnyAsync(t => t.NsgTradeName == name && (!excludeId.HasValue || t.Id != excludeId.Value), cancellationToken);

    public async Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AnyAsync(u => u.NsgTradeId == id, cancellationToken);

    public async Task<bool> AddAsync(NsgTrade entity, CancellationToken cancellationToken = default)
    {
        await _context.NsgTrades.AddAsync(entity, cancellationToken);
        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(NsgTrade entity, CancellationToken cancellationToken = default)
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
