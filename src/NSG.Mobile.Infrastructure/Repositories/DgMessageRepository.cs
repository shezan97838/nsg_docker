using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IDgMessageRepository"/>.</summary>
public class DgMessageRepository : IDgMessageRepository
{
    private readonly AppDbContext _context;

    public DgMessageRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(DgMessage message, CancellationToken cancellationToken = default)
    {
        _context.DgMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<DgMessage> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<DgMessage> query = _context.DgMessages.AsNoTracking().Where(m => m.IsActive && !m.IsDeleted);
        int total = await query.CountAsync(cancellationToken);
        List<DgMessage> items = await query
            .OrderByDescending(m => m.CreatedAtIst).ThenByDescending(m => m.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<DgMessage?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.DgMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.IsActive && !m.IsDeleted, cancellationToken);

    public async Task<DgMessageUpdateResult> UpdateAsync(int id, string dgName, string designation, string message, bool isActive, string? newImageKey,
        long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        DgMessage? entity = await _context.DgMessages.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted, cancellationToken);
        if (entity == null) return new DgMessageUpdateResult(DgMessageUpdateOutcome.NotFound);

        string? previous = entity.ImageStorageKey;
        entity.DgName = dgName;
        entity.Designation = designation;
        entity.Message = message;
        entity.IsActive = isActive;
        if (newImageKey != null) entity.ImageStorageKey = newImageKey;   // null keeps the existing image
        entity.UpdatedBy = updatedBy;
        entity.UpdatedAtIst = nowIst;
        await _context.SaveChangesAsync(cancellationToken);
        return new DgMessageUpdateResult(DgMessageUpdateOutcome.Updated, entity, previous);
    }

    public async Task<DgMessageDeleteOutcome> SoftDeleteAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        // Single conditional statement: of two simultaneous requests only one changes the row. The row and its image stay.
        int changed = await _context.DgMessages
            .Where(m => m.Id == id && !m.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDeleted, true)
                .SetProperty(m => m.UpdatedBy, deletedBy)
                .SetProperty(m => m.UpdatedAtIst, (DateTime?)nowIst), cancellationToken);
        if (changed == 1) return DgMessageDeleteOutcome.Deleted;

        bool exists = await _context.DgMessages.AnyAsync(m => m.Id == id, cancellationToken);
        return exists ? DgMessageDeleteOutcome.AlreadyDeleted : DgMessageDeleteOutcome.NotFound;
    }
}
