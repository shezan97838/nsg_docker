using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Data access for the NSG rank hierarchy.</summary>
public interface IRankHierarchyRepository
{
    Task<RankHierarchy?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Paged list ordered by SrNo. <paramref name="isActive"/> null returns all.</summary>
    Task<(IReadOnlyList<RankHierarchy> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default);

    /// <summary>All active ranks ordered by SrNo (for dropdowns).</summary>
    Task<IReadOnlyList<RankHierarchy>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> SrNoExistsAsync(int srNo, int? excludeId, CancellationToken cancellationToken = default);
    Task<bool> RankNameExistsAsync(string rankName, int? excludeId, CancellationToken cancellationToken = default);

    Task AddAsync(RankHierarchy rank, CancellationToken cancellationToken = default);
    Task UpdateAsync(RankHierarchy rank, CancellationToken cancellationToken = default);
    /// <summary>True when any UsersPIS record is assigned this rank.</summary>
    Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default);

}
