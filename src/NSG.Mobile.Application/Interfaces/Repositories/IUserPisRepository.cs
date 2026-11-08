using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Result of saving a personnel record, distinguishing which unique constraint (if any) rejected it.</summary>
public enum PisSaveOutcome
{
    Saved = 0,
    DuplicateNsgNumber,
    DuplicateMobileNumber,

    /// <summary>The record is no longer assigned to the calling admin (it was transferred since it was loaded); nothing was saved.</summary>
    NotOwner
}

/// <summary>Data access for personnel (PIS) records. Hard deletion is intentionally not offered.</summary>
public interface IUserPisRepository
{
    /// <summary>Tracked entity (no navigation properties) for update and deactivation.</summary>
    Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked entity (no navigation properties) found by NSG number; used by mobile registration. Null when absent.</summary>
    Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default);

    /// <summary>Untracked entity for the detail response.</summary>
    Task<UserPis?> GetDetailAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked page ordered by NSG number, restricted in the database to the records assigned to <paramref name="assignedTo"/> (the calling PIS Admin): search, filters, paging and the total can never reveal another admin's or an unassigned record.</summary>
    Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, long assignedTo, CancellationToken cancellationToken = default);

    /// <summary>Inserts the record; a unique-key violation is reported as an outcome, not thrown.</summary>
    Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves changes to a tracked record that the calling PIS Admin owns; a unique-key violation is reported as an outcome, not thrown.
    /// The save runs in a transaction that first locks the personnel row conditionally on <c>assigned_to = <paramref name="ownerAdminId"/></c>, so an
    /// ownership transfer that committed after the record was loaded is refused (<see cref="PisSaveOutcome.NotOwner"/>, nothing saved).
    /// </summary>
    Task<PisSaveOutcome> UpdateAsync(UserPis entity, long ownerAdminId, CancellationToken cancellationToken = default);
}
