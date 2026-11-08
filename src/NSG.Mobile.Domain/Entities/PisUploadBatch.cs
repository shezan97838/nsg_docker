using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// One PIS Excel upload attempt by one PIS Admin, whatever its outcome. Table <c>pis_upload_batches</c>.
/// Counting rule: every non-blank worksheet row is counted exactly once, as inserted, updated, failed or conflict, so
/// <c>TotalRows = InsertedCount + UpdatedCount + FailedCount + ConflictCount</c> for a processed file.
/// </summary>
public class PisUploadBatch
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public PisUploadType UploadType { get; set; }
    public PisUploadStatus Status { get; set; } = PisUploadStatus.Processing;

    public int TotalRows { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }

    /// <summary>Rows rejected for invalid data, an unknown NSG number (update-only) or a processing error.</summary>
    public int FailedCount { get; set; }

    /// <summary>Rows rejected because of a duplicate NSG number or mobile number.</summary>
    public int ConflictCount { get; set; }

    /// <summary>Client-safe reason when the whole file was refused; null otherwise.</summary>
    public string? FailureMessage { get; set; }

    /// <summary>Admin portal account (<c>admin_profiles.id</c>) that uploaded the file, taken from the token. No foreign key.</summary>
    public long UploadedBy { get; set; }

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public DateTime? CompletedAtIst { get; set; }

    public List<PisUploadBatchRecord> Records { get; set; } = new();
    public List<PisUploadBatchError> Errors { get; set; } = new();
}

/// <summary>
/// A personnel record inserted or updated by a batch. Table <c>pis_upload_batch_records</c>. It lets an admin find every
/// record they touched, including records created by another admin. <see cref="PisRecordId"/> has no foreign key because
/// <c>users_pis</c> is deliberately free of foreign keys in either direction.
/// </summary>
public class PisUploadBatchRecord
{
    public long Id { get; set; }
    public long BatchId { get; set; }

    /// <summary>Worksheet row number (row 1 = main headings; data starts at row 4).</summary>
    public int RowNumber { get; set; }

    public long PisRecordId { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public PisRowOperation Operation { get; set; }
    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
}

/// <summary>A row-level rejection of a batch. Table <c>pis_upload_batch_errors</c>. Values of sensitive fields are never stored.</summary>
public class PisUploadBatchError
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public int RowNumber { get; set; }
    public string? NsgNumber { get; set; }
    public PisRowStatus Status { get; set; }
    public string ConflictType { get; set; } = string.Empty;
    public string? Field { get; set; }

    /// <summary>The offending value, truncated; null for the four sensitive identifiers.</summary>
    public string? Value { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ConflictingNsgNumber { get; set; }
    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
}
