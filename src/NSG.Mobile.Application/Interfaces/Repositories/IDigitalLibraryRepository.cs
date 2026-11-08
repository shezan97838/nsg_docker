using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Focused data access of the Digital Library (<c>digital_library</c>).</summary>
public interface IDigitalLibraryRepository
{
    Task AddAsync(DigitalLibraryItem item, CancellationToken cancellationToken);

    /// <summary>Any record, active or not, read-only.</summary>
    Task<DigitalLibraryItem?> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Any record, tracked for update.</summary>
    Task<DigitalLibraryItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken);

    /// <summary>Saves changes made to a record returned by <see cref="GetForUpdateAsync"/>.</summary>
    Task UpdateAsync(DigitalLibraryItem item, CancellationToken cancellationToken);

    /// <summary>All records (active and inactive) matching the filters, newest first (created time, then id), paged. <paramref name="fileTypeLabel"/> is the normalized filter value (PDF, JPG, JPEG, PNG) or null.</summary>
    Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetAdminPagedAsync(DigitalLibraryAdminFilter filter, string? fileTypeLabel, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Active records only, newest first (created time, then id), paged.</summary>
    Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetActivePagedAsync(bool? isImportant, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The record when it exists and is active; otherwise null.</summary>
    Task<DigitalLibraryItem?> GetActiveByIdAsync(long id, CancellationToken cancellationToken);
}
