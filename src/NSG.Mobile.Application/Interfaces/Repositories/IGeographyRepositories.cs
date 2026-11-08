using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Data access for States and Union Territories.</summary>
public interface IStateRepository
{
    Task<State?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<State> Items, int TotalCount)> GetPagedAsync(StateListQuery query, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default);

    Task AddAsync(State state, CancellationToken cancellationToken = default);
    Task UpdateAsync(State state, CancellationToken cancellationToken = default);
    Task DeleteAsync(State state, CancellationToken cancellationToken = default);
}
