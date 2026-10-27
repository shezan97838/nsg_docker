using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Leave taken by one person in one calendar year, maintained by Leave Admins. Table <c>personnel_leave_records</c>; unique on
/// (<see cref="NsgNumber"/>, <see cref="LeaveYear"/>). <see cref="LeaveTaken"/> is the authoritative TOTAL for the year, not an increment.
/// There is no foreign key to <c>users_pis</c>: the NSG number is validated against it by the service (like <c>admin_profiles</c>), and a year's
/// row is never deleted or overwritten when a new year starts. <see cref="CreatedBy"/>/<see cref="UpdatedBy"/> are <c>admin_profiles.id</c> values
/// taken from the token (no foreign key, like the other admin id columns). On creation the update fields equal the creation fields.
/// </summary>
public class PersonnelLeaveRecord
{
    public long Id { get; set; }

    /// <summary>NSG number exactly as stored in <c>users_pis.nsg_number</c>.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Calendar year (IST) the count belongs to.</summary>
    public int LeaveYear { get; set; }

    /// <summary>Total leave days taken in <see cref="LeaveYear"/>; never negative.</summary>
    public int LeaveTaken { get; set; }

    /// <summary>Optimistic-concurrency token, incremented by every change so two simultaneous writers cannot silently overwrite each other.</summary>
    public int RowVersion { get; set; } = 1;

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public long CreatedBy { get; set; }
    public DateTime UpdatedAtIst { get; set; } = IndianStandardTime.Now;
    public long UpdatedBy { get; set; }
}

/// <summary>
/// Append-only audit of every actual change of a leave count. Table <c>personnel_leave_history</c>. <see cref="OldLeaveTaken"/> is null when the
/// change created the record. Rows written by a bulk upload carry the batch id; they are written in the same transaction as the leave change.
/// </summary>
public class PersonnelLeaveHistory
{
    public long Id { get; set; }
    public long LeaveRecordId { get; set; }

    /// <summary>Denormalised so the history stays readable on its own.</summary>
    public string NsgNumber { get; set; } = string.Empty;
    public int LeaveYear { get; set; }

    public int? OldLeaveTaken { get; set; }
    public int NewLeaveTaken { get; set; }

    public LeaveChangeSource ChangeSource { get; set; }

    /// <summary>The upload batch for <see cref="LeaveChangeSource.Bulk"/> changes; null for manual changes.</summary>
    public long? UploadBatchId { get; set; }

    public long ChangedBy { get; set; }
    public DateTime ChangedAtIst { get; set; } = IndianStandardTime.Now;
}

/// <summary>
/// One bulk upload attempt, kept whatever its outcome. Table <c>personnel_leave_upload_batches</c>. A batch is written BEFORE any leave change
/// and its final status is written in the same transaction as the leave changes (success) or in a separate step after a rollback (failure), so
/// <see cref="LeaveUploadStatus.Completed"/> always means the data was committed.
/// </summary>
public class PersonnelLeaveUploadBatch
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>Calendar year (IST) the upload applies to, fixed by the server when the upload starts.</summary>
    public int LeaveYear { get; set; }

    public int TotalRows { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }

    /// <summary>Valid rows whose count already equalled the stored value: nothing was written for them.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Rows (or, for a file-level problem, 1) that failed validation or processing.</summary>
    public int FailedCount { get; set; }

    public LeaveUploadStatus UploadStatus { get; set; } = LeaveUploadStatus.Processing;

    /// <summary>
    /// 1 while the batch is <see cref="LeaveUploadStatus.Processing"/>, otherwise NULL. A unique index on this column lets the database itself
    /// refuse a second simultaneous upload (MySQL allows many NULLs in a unique index).
    /// </summary>
    public bool? ProcessingLock { get; set; } = true;

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public long CreatedBy { get; set; }
    public DateTime? CompletedAtIst { get; set; }

    public List<PersonnelLeaveUploadError> Errors { get; set; } = new();
}

/// <summary>A row-level (or file-level, when <see cref="RowNumber"/> is null) problem of a bulk upload. Table <c>personnel_leave_upload_errors</c>.</summary>
public class PersonnelLeaveUploadError
{
    public long Id { get; set; }
    public long BatchId { get; set; }

    /// <summary>Excel row number (the header is row 1); null for a problem with the whole file or with applying it.</summary>
    public int? RowNumber { get; set; }

    /// <summary>The NSG number as typed in the file (trimmed, truncated to the column length); null when the row had none.</summary>
    public string? NsgNumber { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
}
