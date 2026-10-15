using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Result of saving a personnel record, distinguishing which unique constraint (if any) rejected it.</summary>
public enum PisSaveOutcome
{
    Saved = 0,
    DuplicateNsgNumber,
    DuplicateMobileNumber,
    DuplicatePanNumber,
    DuplicateSalaryAccountNumber
}

/// <summary>Data access for personnel (PIS) records. Hard deletion is intentionally not offered.</summary>
public interface IUserPisRepository
{
    /// <summary>Tracked entity (no navigation properties) for update and deactivation.</summary>
    Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked entity (no navigation properties) found by NSG number; used by mobile registration. Null when absent.</summary>
    Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);

    /// <summary>Untracked entity with every lookup navigation loaded, for the detail response.</summary>
    Task<UserPis?> GetDetailAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked page (rank and unit loaded) ordered by NSG number.</summary>
    Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Names of the supplied reference ids that do not exist; empty when all exist.</summary>
    Task<IReadOnlyList<string>> GetMissingReferencesAsync(UserPis candidate, CancellationToken cancellationToken = default);

    /// <summary>Inserts the record; a unique-key violation is reported as an outcome, not thrown.</summary>
    Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken cancellationToken = default);

    /// <summary>Saves changes to a tracked record; a unique-key violation is reported as an outcome, not thrown.</summary>
    Task<PisSaveOutcome> UpdateAsync(UserPis entity, CancellationToken cancellationToken = default);
}
