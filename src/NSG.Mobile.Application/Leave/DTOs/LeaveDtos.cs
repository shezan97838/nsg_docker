namespace NSG.Mobile.Application.Leave.DTOs;

/// <summary>Body of the manual create/update. The year is always the current calendar year (IST), fixed by the server; the acting admin comes from the token.</summary>
public class SaveLeaveRecordRequestDto
{
    /// <summary>NSG number of the person (must exist in the personnel records). Ignored on PUT, where the route carries it.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>The final TOTAL of leave days taken this year (not an increment): a whole number from 0 to 366.</summary>
    public int? LeaveTaken { get; set; }
}

/// <summary>Body of PUT: only the new total (the NSG number is in the route).</summary>
public class UpdateLeaveRecordRequestDto
{
    public int? LeaveTaken { get; set; }
}

/// <summary>A leave record as an administrator sees it.</summary>
public class LeaveRecordAdminDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public int LeaveYear { get; set; }
    public int LeaveTaken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long UpdatedBy { get; set; }
}

/// <summary>The caller's own current-year leave, as the mobile app sees it.</summary>
public class MyLeaveRecordDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public int LeaveYear { get; set; }
    public int LeaveTaken { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

/// <summary>One change of a leave count.</summary>
public class LeaveHistoryDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public int LeaveYear { get; set; }

    /// <summary>Null when the change created the record.</summary>
    public int? OldLeaveTaken { get; set; }
    public int NewLeaveTaken { get; set; }

    /// <summary><c>manual</c> or <c>bulk</c>.</summary>
    public string Source { get; set; } = string.Empty;
    public long? UploadBatchId { get; set; }
    public long ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>A bulk upload batch: its status and counters.</summary>
public class LeaveUploadBatchDto
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int LeaveYear { get; set; }

    /// <summary><c>Processing</c>, <c>Completed</c>, <c>ValidationFailed</c> or <c>ProcessingFailed</c>.</summary>
    public string UploadStatus { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>A row-level problem of a bulk upload. <see cref="RowNumber"/> is null for a problem with the whole file.</summary>
public class LeaveUploadErrorDto
{
    public int? RowNumber { get; set; }
    public string? NsgNumber { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
