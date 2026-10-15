using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Data access for religions (PIS lookup).</summary>
public interface IReligionRepository
{
    /// <summary>Tracked entity for update/delete.</summary>
    Task<Religion?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Paged list ordered by Name. <paramref name="isActive"/> null returns all.</summary>
    Task<(IReadOnlyList<Religion> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default);

    /// <summary>All active records ordered by Name (for dropdowns).</summary>
    Task<IReadOnlyList<Religion>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default);

    /// <summary>True when any UsersPIS record references this religion.</summary>
    Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate Name (unique index).</summary>
    Task<bool> AddAsync(Religion entity, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate Name (unique index).</summary>
    Task<bool> UpdateAsync(Religion entity, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the delete because a foreign key still references the row.</summary>
    Task<bool> DeleteAsync(Religion entity, CancellationToken cancellationToken = default);
}
