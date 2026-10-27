namespace NSG.Mobile.Domain.Enums;

/// <summary>Lifecycle of a leave bulk-upload batch (stored as INT in <c>personnel_leave_upload_batches.upload_status</c>).</summary>
public enum LeaveUploadStatus
{
    /// <summary>The upload is being validated or applied. Holds the single upload lock until it ends.</summary>
    Processing = 1,

    /// <summary>Every row was valid and the leave changes were committed in one transaction.</summary>
    Completed = 2,

    /// <summary>The file or at least one row was invalid. No leave record was changed.</summary>
    ValidationFailed = 3,

    /// <summary>The file was valid but applying it failed. The transaction was rolled back, or the batch was abandoned; no leave record was changed.</summary>
    ProcessingFailed = 4
}

/// <summary>How a leave count was changed (stored as INT in <c>personnel_leave_history.change_source</c>).</summary>
public enum LeaveChangeSource
{
    Manual = 1,
    Bulk = 2
}
