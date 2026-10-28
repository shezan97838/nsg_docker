using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IBannerRepository"/>.</summary>
public class BannerRepository : IBannerRepository
{
    private readonly AppDbContext _context;

    public BannerRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(Banner banner, CancellationToken cancellationToken = default)
    {
        _context.Banners.Add(banner);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Banner?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Banners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<Banner?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Banners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.IsActive, cancellationToken);


    public async Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetAllPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<Banner> query = _context.Banners.AsNoTracking();
        int total = await query.CountAsync(cancellationToken);
        List<Banner> items = await query
            .OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<Banner> query = _context.Banners.AsNoTracking()
            .Where(b => b.IsActive);
        int total = await query.CountAsync(cancellationToken);
        List<Banner> items = await query
            .OrderBy(b => b.Priority == null).ThenBy(b => b.Priority).ThenByDescending(b => b.Id)   // nulls last, then newest
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<BannerUpdateResult> UpdateAsync(int id, BannerValues values, string? newImageKey, bool removeImage, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Banner? entity = await _context.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null) return new BannerUpdateResult(null);

        string? previous = entity.ImageStorageKey;

        entity.Title = values.Title;
        entity.Description = values.Description;
        entity.Category = values.Category;
        if (newImageKey != null) entity.ImageStorageKey = newImageKey;
        else if (removeImage) entity.ImageStorageKey = null;
        entity.ImageAltText = entity.ImageStorageKey == null ? null : values.ImageAltText;   // alt text only describes an image
        entity.ShowOnlyImage = entity.ImageStorageKey != null && values.ShowOnlyImage;       // image-only needs an image (also a CHECK constraint)
        entity.NsgUnitId = values.NsgUnitId;
        entity.Priority = values.Priority;
        entity.IsActive = values.IsActive;
        entity.UpdatedAtIst = nowIst;
        await _context.SaveChangesAsync(cancellationToken);
        return new BannerUpdateResult(entity, previous);
    }

    public async Task<BannerStatusResult> SetStatusAsync(int id, bool isActive, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Banner? entity = await _context.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null) return new BannerStatusResult(BannerStatusOutcome.NotFound);
        if (entity.IsActive == isActive) return new BannerStatusResult(BannerStatusOutcome.Unchanged, entity);

        entity.IsActive = isActive;
        entity.UpdatedAtIst = nowIst;
        await _context.SaveChangesAsync(cancellationToken);
        return new BannerStatusResult(BannerStatusOutcome.Changed, entity);
    }
}
