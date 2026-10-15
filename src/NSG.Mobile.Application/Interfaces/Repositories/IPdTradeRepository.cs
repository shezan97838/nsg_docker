using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Data access for PD trades. Hard deletion is intentionally not offered: trades are deactivated.</summary>
public interface IPdTradeRepository
{
    /// <summary>Tracked trade, for update and deactivation.</summary>
    Task<PdTrade?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Untracked page ordered by name. <paramref name="isActive"/> null returns all.</summary>
    Task<(IReadOnlyList<PdTrade> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default);

    /// <summary>True when any UsersPIS record uses this trade.</summary>
    Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate name (unique index).</summary>
    Task<bool> AddAsync(PdTrade entity, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate name (unique index).</summary>
    Task<bool> UpdateAsync(PdTrade entity, CancellationToken cancellationToken = default);
}
