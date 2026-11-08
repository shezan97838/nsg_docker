using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class PisAdminQueryRepository : IPisAdminQueryRepository
{
    private readonly AppDbContext _context;

    public PisAdminQueryRepository(AppDbContext context) => _context = context;

    public Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken)
        => _context.UsersPis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken)
        => _context.UsersPis.AsNoTracking().FirstOrDefaultAsync(p => p.NsgNumber == nsgNumber, cancellationToken);

    public async Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, PisAdminQuery query, long assignedTo, CancellationToken cancellationToken)
    {
        IQueryable<UserPis> filtered = Filter(query, assignedTo);
        int total = await filtered.CountAsync(cancellationToken);
        List<UserPis> items = await Sort(filtered, query).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountAsync(PisAdminQuery query, long assignedTo, CancellationToken cancellationToken)
        => Filter(query, assignedTo).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<UserPis>> GetAllAsync(PisAdminQuery query, long assignedTo, CancellationToken cancellationToken)
        => await Sort(Filter(query, assignedTo), query).ToListAsync(cancellationToken);

    /// <summary>
    /// The ownership restriction is the FIRST predicate and is shared by the page, the total and the unpaged list, so a record assigned to another
    /// admin or to nobody (<c>assigned_to IS NULL</c>) is never counted, searched, filtered, sorted or returned.
    /// </summary>
    private IQueryable<UserPis> Filter(PisAdminQuery q, long assignedTo)
    {
        IQueryable<UserPis> query = _context.UsersPis.AsNoTracking().Where(p => p.AssignedTo == assignedTo);

        if (q.IsActive.HasValue) query = query.Where(p => p.IsActive == q.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(q.NsgNumber))
        {
            string nsg = q.NsgNumber.Trim();
            query = query.Where(p => p.NsgNumber == nsg);
        }
        if (!string.IsNullOrWhiteSpace(q.Unit))
        {
            string unit = q.Unit.Trim();
            query = query.Where(p => p.NsgUnit == unit);
        }
        if (!string.IsNullOrWhiteSpace(q.Rank))
        {
            string rank = q.Rank.Trim();
            query = query.Where(p => p.NsgRank == rank);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            string term = q.Search.Trim();
            query = query.Where(p => p.NsgNumber.Contains(term) || (p.ServiceNumber != null && p.ServiceNumber.Contains(term))
                || (p.FullName != null && p.FullName.Contains(term)) || p.MobileNumber.Contains(term));
        }

        return query;
    }

    /// <summary>Whitelisted sort with the primary key as the final tie-breaker, so paging is deterministic.</summary>
    private static IOrderedQueryable<UserPis> Sort(IQueryable<UserPis> query, PisAdminQuery q)
    {
        bool desc = string.Equals(q.SortOrder?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        string key = (q.SortBy ?? "nsgNumber").Trim().ToLowerInvariant();

        IOrderedQueryable<UserPis> ordered = key switch
        {
            "fullname" => desc ? query.OrderByDescending(p => p.FullName) : query.OrderBy(p => p.FullName),
            "nsgrank" => desc ? query.OrderByDescending(p => p.NsgRank) : query.OrderBy(p => p.NsgRank),
            "nsgunit" => desc ? query.OrderByDescending(p => p.NsgUnit) : query.OrderBy(p => p.NsgUnit),
            "servicenumber" => desc ? query.OrderByDescending(p => p.ServiceNumber) : query.OrderBy(p => p.ServiceNumber),
            "mobilenumber" => desc ? query.OrderByDescending(p => p.MobileNumber) : query.OrderBy(p => p.MobileNumber),
            "dateofbirth" => desc ? query.OrderByDescending(p => p.DateOfBirth) : query.OrderBy(p => p.DateOfBirth),
            "dateofinductioninnsg" => desc ? query.OrderByDescending(p => p.DateOfInductionInNsg) : query.OrderBy(p => p.DateOfInductionInNsg),
            "id" => desc ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
            _ => desc ? query.OrderByDescending(p => p.NsgNumber) : query.OrderBy(p => p.NsgNumber)
        };

        return desc ? ordered.ThenByDescending(p => p.Id) : ordered.ThenBy(p => p.Id);
    }
}
