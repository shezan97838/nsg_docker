using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class NsgUnitRepository : INsgUnitRepository
{
    private readonly AppDbContext _context;

    public NsgUnitRepository(AppDbContext context) => _context = context;

    public async Task<NsgUnit?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.NsgUnits.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<NsgUnit> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        IQueryable<NsgUnit> query = _context.NsgUnits.AsNoTracking();

        if (isActive.HasValue)
            query = query.Where(l => l.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(l => l.Name.Contains(term));
        }

        int total = await query.CountAsync(cancellationToken);
        List<NsgUnit> items = await query
            .OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<NsgUnit>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await _context.NsgUnits.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.NsgUnits.AnyAsync(l => l.Name == name && (!excludeId.HasValue || l.Id != excludeId.Value), cancellationToken);

    public async Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default)
        => await _context.UsersPis.AnyAsync(u => u.NsgUnitId == id || u.SideSteppedFromNsgUnitId == id, cancellationToken);

    public async Task<bool> AddAsync(NsgUnit entity, CancellationToken cancellationToken = default)
    {
        await _context.NsgUnits.AddAsync(entity, cancellationToken);
        return await TrySaveAsync(isDelete: false, cancellationToken);
    }

    public async Task<bool> UpdateAsync(NsgUnit entity, CancellationToken cancellationToken = default)
    {
        _context.NsgUnits.Update(entity);
        return await TrySaveAsync(isDelete: false, cancellationToken);
    }

    public async Task<bool> DeleteAsync(NsgUnit entity, CancellationToken cancellationToken = default)
    {
        _context.NsgUnits.Remove(entity);
        return await TrySaveAsync(isDelete: true, cancellationToken);
    }

    /// <summary>
    /// Saves, translating the one expected MySQL constraint violation (duplicate key on write, referenced row on delete)
    /// into <c>false</c>; any other failure propagates to the global exception handler.
    /// </summary>
    private async Task<bool> TrySaveAsync(bool isDelete, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysql && IsExpected(mysql.ErrorCode, isDelete))
        {
            return false;
        }
    }

    private static bool IsExpected(MySqlErrorCode code, bool isDelete)
        => isDelete
            ? code is MySqlErrorCode.RowIsReferenced or MySqlErrorCode.RowIsReferenced2
            : code == MySqlErrorCode.DuplicateKeyEntry;
}
