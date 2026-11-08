using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>A Form 16 record with the name of the person from the personnel record (null when that record is gone).</summary>
public sealed record Form16Row(PersonnelForm16 Form16, string? EmployeeName);

/// <summary>Result of inserting a Form 16 record.</summary>
public enum Form16InsertOutcome
{
    Inserted,

    /// <summary>A record for the same NSG number and assessment year already exists (lost a race with another upload).</summary>
    Duplicate
}

/// <summary>Focused data access of the Form 16 module (<c>personnel_form16</c>).</summary>
public interface IForm16Repository
{
    /// <summary>The record for the NSG number and assessment year, tracked for update; null if there is none.</summary>
    Task<PersonnelForm16?> GetByKeyForUpdateAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken);

    /// <summary>The record for the NSG number and assessment year, read only; null if there is none.</summary>
    Task<PersonnelForm16?> GetByKeyAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken);

    /// <summary>One record by id with the person's name; null if there is none.</summary>
    Task<Form16Row?> GetRowByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Records, newest assessment year first, optionally filtered by NSG number and/or assessment year.</summary>
    Task<(IReadOnlyList<Form16Row> Items, int TotalCount)> GetPagedAsync(string? nsgNumber, string? assessmentYear, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>All records of one person, newest assessment year first.</summary>
    Task<IReadOnlyList<PersonnelForm16>> ListByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken);

    Task<Form16InsertOutcome> AddAsync(PersonnelForm16 entity, CancellationToken cancellationToken);

    /// <summary>Saves the changes of a record obtained from <see cref="GetByKeyForUpdateAsync"/>.</summary>
    Task UpdateAsync(PersonnelForm16 entity, CancellationToken cancellationToken);
}
