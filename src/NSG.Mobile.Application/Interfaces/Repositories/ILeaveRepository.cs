using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Result of a manual save of one leave record.</summary>
public enum LeaveSaveOutcome
{
    Created = 1,
    Updated,

    /// <summary>The stored total already equals the requested one: nothing was written (no history row, no audit-field change).</summary>
    Unchanged,

    /// <summary>The caller asked for an update of a record that does not exist.</summary>
    NotFound,

    /// <summary>Another writer kept winning the race for this record; nothing was written.</summary>
    ConcurrencyConflict
}

public sealed record LeaveSaveResult(LeaveSaveOutcome Outcome, PersonnelLeaveRecord? Record, int? OldLeaveTaken);

/// <summary>A validated bulk row: the NSG number is the canonical value stored in the personnel records.</summary>
public sealed record LeaveBulkRow(int RowNumber, string NsgNumber, int LeaveTaken);

/// <summary>A problem recorded against a batch (<paramref name="RowNumber"/> null = whole file / processing).</summary>
public sealed record LeaveUploadIssue(int? RowNumber, string? NsgNumber, string Message);

/// <summary>What is known about the leave transaction of a bulk upload once <see cref="ILeaveRepository.ApplyBulkAsync"/> returns.</summary>
public enum LeaveBulkOutcome
{
    /// <summary>Committed: the leave records, history rows and the batch (status Completed) were written together.</summary>
    Committed = 1,

    /// <summary>Rolled back because a record was created or changed by someone else meanwhile. Nothing was saved.</summary>
    RolledBackConflict,

    /// <summary>Rolled back because of a database failure, verified by reading the batch afterwards. Nothing was saved.</summary>
    RolledBackFailure,

    /// <summary>The transaction failed and the outcome could not be verified either (database unreachable). The batch stays Processing.</summary>
    Unknown
}

public sealed record LeaveBulkResult(LeaveBulkOutcome Outcome, int Created, int Updated, int Unchanged);

/// <summary>
/// Data access for personnel leave. Every multi-row write is one transaction; reads are untracked. Timestamps are IST instants supplied by the
/// caller. The repository never decides authorization and never trusts a year: the caller passes the server-derived one.
/// </summary>
public interface ILeaveRepository
{
    // ── Personnel ──

    /// <summary>
    /// The stored spelling of every supplied NSG number that belongs to an ACTIVE personnel record (match ignores case), keyed by the supplied
    /// spelling's upper-case form. Missing keys are unknown or inactive people.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetActivePersonnelNsgNumbersAsync(IReadOnlyCollection<string> nsgNumbers, CancellationToken cancellationToken = default);

    /// <summary>NSG number of the mobile profile (null when the profile does not exist).</summary>
    Task<string?> GetNsgNumberOfMobileProfileAsync(long mobileProfileId, CancellationToken cancellationToken = default);

    // ── Records ──

    Task<PersonnelLeaveRecord?> GetRecordAsync(string nsgNumber, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates (when <paramref name="createIfMissing"/>) or updates the record for the year and writes the history row in the same transaction.
    /// Safe under concurrency: a lost race on the unique key or on the row version is retried, then reported as a conflict.
    /// </summary>
    Task<LeaveSaveResult> SaveManualAsync(string nsgNumber, int year, int leaveTaken, bool createIfMissing, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PersonnelLeaveHistory> Items, int TotalCount)> GetHistoryPagedAsync(string nsgNumber, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    // ── Bulk upload ──

    /// <summary>
    /// Writes a new batch in status Processing holding the single upload lock. Returns its id, or null when another upload is running (abandoned
    /// batches older than <paramref name="staleAfter"/> are first marked ProcessingFailed so they cannot block forever).
    /// </summary>
    Task<long?> TryStartBatchAsync(string fileName, int year, long adminProfileId, DateTime nowIst, TimeSpan staleAfter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the validated rows in ONE transaction: creates missing records, replaces changed totals, writes the history rows and finally marks the
    /// batch Completed with its counters. All or nothing. Never throws for a database failure: it reports <see cref="LeaveBulkOutcome"/>, verifying
    /// the real state of the batch when the outcome is uncertain.
    /// </summary>
    Task<LeaveBulkResult> ApplyBulkAsync(long batchId, int year, int totalRows, IReadOnlyList<LeaveBulkRow> rows, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a batch that did not change any leave record (ValidationFailed or ProcessingFailed): stores the issues, the counters, releases the
    /// lock. Does nothing to a batch that is not Processing any more (so it can never overwrite a Completed batch).
    /// </summary>
    Task FinishFailedBatchAsync(long batchId, LeaveUploadStatus status, int totalRows, int failedCount, IReadOnlyList<LeaveUploadIssue> issues, DateTime nowIst, CancellationToken cancellationToken = default);

    Task<PersonnelLeaveUploadBatch?> GetBatchAsync(long batchId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PersonnelLeaveUploadBatch> Items, int TotalCount)> GetBatchesPagedAsync(int pageNumber, int pageSize, LeaveUploadStatus? status, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PersonnelLeaveUploadError> Items, int TotalCount)> GetBatchErrorsPagedAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
