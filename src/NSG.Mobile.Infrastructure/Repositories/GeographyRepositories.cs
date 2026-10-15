using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class StateRepository : IStateRepository
{
    private readonly AppDbContext _context;

    public StateRepository(AppDbContext context) => _context = context;

    public async Task<State?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.States.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
        => await _context.States.AnyAsync(s => s.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<State> Items, int TotalCount)> GetPagedAsync(StateListQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<State> q = _context.States.AsNoTracking();

        if (query.AdministrativeType.HasValue)
            q = q.Where(s => s.AdministrativeType == query.AdministrativeType.Value);
        if (query.IsActive.HasValue)
            q = q.Where(s => s.IsActive == query.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            q = q.Where(s => s.Name.Contains(term) || (s.Code != null && s.Code.Contains(term)));
        }

        int total = await q.CountAsync(cancellationToken);

        bool desc = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<State> ordered = (query.SortBy?.ToLowerInvariant(), desc) switch
        {
            ("code", false) => q.OrderBy(s => s.Code),
            ("code", true) => q.OrderByDescending(s => s.Code),
            ("createdat", false) => q.OrderBy(s => s.CreatedAtIst),
            ("createdat", true) => q.OrderByDescending(s => s.CreatedAtIst),
            (_, true) => q.OrderByDescending(s => s.Name),
            _ => q.OrderBy(s => s.Name)
        };

        // Id tie-breaker keeps paging deterministic.
        List<State> items = await ordered.ThenBy(s => s.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.States.AnyAsync(s => s.Code == code && (!excludeId.HasValue || s.Id != excludeId.Value), cancellationToken);

    public async Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.States.AnyAsync(s => s.Name == name && (!excludeId.HasValue || s.Id != excludeId.Value), cancellationToken);

    public async Task<bool> HasCitiesAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Cities.AnyAsync(c => c.StateId == id, cancellationToken);

    public async Task AddAsync(State state, CancellationToken cancellationToken = default)
    {
        await _context.States.AddAsync(state, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(State state, CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(State state, CancellationToken cancellationToken = default)
    {
        _context.States.Remove(state);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class CityRepository : ICityRepository
{
    private readonly AppDbContext _context;

    public CityRepository(AppDbContext context) => _context = context;

    public async Task<City?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Cities.Include(c => c.State).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<City> Items, int TotalCount)> GetPagedAsync(CityListQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<City> q = _context.Cities.AsNoTracking();

        if (query.StateId.HasValue)
            q = q.Where(c => c.StateId == query.StateId.Value);
        if (query.Classification.HasValue)
            q = q.Where(c => c.Classification == query.Classification.Value);
        if (query.IsActive.HasValue)
            q = q.Where(c => c.IsActive == query.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();
            q = q.Where(c => c.Name.Contains(term));
        }

        int total = await q.CountAsync(cancellationToken);

        bool desc = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<City> ordered = (query.SortBy?.ToLowerInvariant(), desc) switch
        {
            ("createdat", false) => q.OrderBy(c => c.CreatedAtIst),
            ("createdat", true) => q.OrderByDescending(c => c.CreatedAtIst),
            (_, true) => q.OrderByDescending(c => c.Name),
            _ => q.OrderBy(c => c.Name)
        };

        // Single query with a join for the state name — no N+1.
        List<City> items = await ordered.ThenBy(c => c.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(c => c.State)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<bool> DuplicateExistsAsync(int stateId, string name, TownClassification? classification, int? excludeId, CancellationToken cancellationToken = default)
        => await _context.Cities.AnyAsync(c =>
            c.StateId == stateId && c.Name == name && c.Classification == classification
            && (!excludeId.HasValue || c.Id != excludeId.Value), cancellationToken);

    public async Task AddAsync(City city, CancellationToken cancellationToken = default)
    {
        await _context.Cities.AddAsync(city, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(City city, CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(City city, CancellationToken cancellationToken = default)
    {
        _context.Cities.Remove(city);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
