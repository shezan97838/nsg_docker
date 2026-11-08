using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum PisRowSaveStatus
{
    Saved = 0,
    DuplicateNsgNumber,
    DuplicateMobileNumber,

    /// <summary>An update targeted a record that no longer exists.</summary>
    NotFound,

    /// <summary>An update targeted a record that is not assigned to the uploading admin (checked inside the row's own transaction); nothing was changed.</summary>
    NotOwner,

    /// <summary>Any other database failure; nothing of the row was saved.</summary>
    Failed
}

/// <summary>Outcome of saving one row. <see cref="ConflictingNsgNumber"/> is set for a mobile-number conflict when the owner could be found.</summary>
public sealed record PisRowSaveResult(PisRowSaveStatus Status, long? RecordId = null, string? ConflictingNsgNumber = null);

/// <summary>Data access of the PIS Excel upload and its batch history.</summary>
public interface IPisImportRepository
{
    /// <summary>Persists a new batch (status Processing) and returns its id.</summary>
    Task<long> CreateBatchAsync(PisUploadBatch batch, CancellationToken cancellationToken);

    /// <summary>Existing personnel by NSG number (case-insensitive keys), untracked: used to decide updates and the mandatory-field rule.</summary>
    Task<IReadOnlyDictionary<string, UserPis>> GetByNsgNumbersAsync(IReadOnlyCollection<string> nsgNumbers, CancellationToken cancellationToken);

    /// <summary>Owning NSG number of each mobile number that is already assigned.</summary>
    Task<IReadOnlyDictionary<string, string>> GetNsgNumbersByMobileAsync(IReadOnlyCollection<string> mobileNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Saves ONE row in its own transaction together with its batch-record mapping, so a failing row never affects another.
    /// Insert: <paramref name="values"/> is added. Update: only the personnel columns are written (never the NSG number, creation
    /// metadata, activation status, is_active or <c>assigned_to</c>) and <c>values.Id</c> names the record.
    /// <paramref name="adminId"/> is the authenticated admin's <c>admin_profiles.id</c>: an insert stores it as <c>assigned_to</c> in the same
    /// transaction; an update is applied only if the record is assigned to that admin, verified under a row lock in the same transaction
    /// (otherwise <see cref="PisRowSaveStatus.NotOwner"/> and nothing is changed).
    /// </summary>
    Task<PisRowSaveResult> SaveRowAsync(long batchId, int rowNumber, UserPis values, bool insert, long adminId, DateTime nowIst, CancellationToken cancellationToken);

    /// <summary>Writes the final counters/status of the batch (row-level errors are saved separately, per row, by <see cref="SaveErrorsAsync"/>).</summary>
    Task CompleteBatchAsync(PisUploadBatch summary, CancellationToken cancellationToken);

    /// <summary>Saves the error rows of ONE rejected worksheet row in their own transaction.</summary>
    Task SaveErrorsAsync(IReadOnlyList<PisUploadBatchError> errors, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PisUploadBatch> Items, int TotalCount)> GetBatchesAsync(long adminId, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The batch only when it was uploaded by <paramref name="adminId"/>; otherwise null.</summary>
    Task<PisUploadBatch?> GetBatchAsync(long adminId, long batchId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PisUploadBatchRecord> Items, int TotalCount)> GetBatchRecordsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PisUploadBatchError> Items, int TotalCount)> GetBatchErrorsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
