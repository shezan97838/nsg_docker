using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class DigitalLibraryRepository : IDigitalLibraryRepository
{
    private readonly AppDbContext _context;

    public DigitalLibraryRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(DigitalLibraryItem item, CancellationToken cancellationToken)
    {
        _context.DigitalLibrary.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<DigitalLibraryItem?> GetByIdAsync(long id, CancellationToken cancellationToken)
        => _context.DigitalLibrary.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DigitalLibraryItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken)
        => _context.DigitalLibrary.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task UpdateAsync(DigitalLibraryItem item, CancellationToken cancellationToken)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetAdminPagedAsync(
        DigitalLibraryAdminFilter filter, string? fileTypeLabel, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<DigitalLibraryItem> query = _context.DigitalLibrary.AsNoTracking();
        if (!string.IsNullOrEmpty(filter.Search)) query = query.Where(d => d.Description.Contains(filter.Search));
        if (filter.IsActive != null) query = query.Where(d => d.IsActive == filter.IsActive.Value);
        if (filter.IsImportant != null) query = query.Where(d => d.IsImportant == filter.IsImportant.Value);
        if (fileTypeLabel != null) query = query.Where(d => d.FileType == fileTypeLabel);

        return await PageAsync(query, pageNumber, pageSize, cancellationToken);
    }

    public async Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetActivePagedAsync(bool? isImportant, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<DigitalLibraryItem> query = _context.DigitalLibrary.AsNoTracking().Where(d => d.IsActive);
        if (isImportant != null) query = query.Where(d => d.IsImportant == isImportant.Value);

        return await PageAsync(query, pageNumber, pageSize, cancellationToken);
    }

    public Task<DigitalLibraryItem?> GetActiveByIdAsync(long id, CancellationToken cancellationToken)
        => _context.DigitalLibrary.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);

    private static async Task<(IReadOnlyList<DigitalLibraryItem>, int)> PageAsync(IQueryable<DigitalLibraryItem> query, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        int total = await query.CountAsync(cancellationToken);
        List<DigitalLibraryItem> items = await query.OrderByDescending(d => d.CreatedAtIst).ThenByDescending(d => d.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }
}
