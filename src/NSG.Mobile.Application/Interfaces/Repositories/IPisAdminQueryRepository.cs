using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>
/// Untracked, database-filtered reads of complete personnel records for the admin API. Every list, count and page is restricted in the
/// database to the records assigned to the calling PIS Admin (<c>assigned_to = assignedTo</c>), so search, filters, sorting, paging and totals
/// can never reveal another admin's records. The two single-record reads return the row whatever its owner; the service decides what the caller may see.
/// </summary>
public interface IPisAdminQueryRepository
{
    Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken);
    Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, PisAdminQuery query, long assignedTo, CancellationToken cancellationToken);
    Task<int> CountAsync(PisAdminQuery query, long assignedTo, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserPis>> GetAllAsync(PisAdminQuery query, long assignedTo, CancellationToken cancellationToken);
}
