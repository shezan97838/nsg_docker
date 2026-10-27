namespace NSG.Mobile.Application.Leave;

/// <summary>
/// Tunables of the Leave module. Built once at startup by the composition root from the <c>Leave</c> configuration section (Options pattern);
/// services never read configuration directly. The defaults suit the expected 200-500 row workbooks with plenty of headroom.
/// </summary>
public class LeaveSettings
{
    /// <summary>Largest accepted workbook, in bytes (default 2 MB). The workbook is held in memory, so this bounds memory use.</summary>
    public long MaxFileSizeBytes { get; set; } = 2L * 1024 * 1024;

    /// <summary>Most data rows (the header excluded) one workbook may carry.</summary>
    public int MaxRows { get; set; } = 5000;

    /// <summary>A batch still Processing after this many minutes is treated as abandoned (crashed host) and no longer blocks new uploads.</summary>
    public int StaleBatchMinutes { get; set; } = 15;
}

/// <summary>Fixed limits and format rules of the Leave module.</summary>
public static class LeaveLimits
{
    public const int NsgNumberMaxLength = 30;

    /// <summary>Largest accepted leave count for one year (a calendar year has at most 366 days). OPEN QUESTION OQ-040.</summary>
    public const int MaxLeaveTaken = 366;

    public const int FileNameMaxLength = 255;
    public const int ErrorMessageMaxLength = 500;

    /// <summary>At most this many error rows are stored per batch; a final row says so when more were found.</summary>
    public const int MaxStoredErrors = 1000;

    /// <summary>How often a manual write that loses a race with another writer is retried before it is reported as a conflict.</summary>
    public const int MaxManualAttempts = 3;

    public const string TemplateNsgColumn = "NSGNumber";
    public const string TemplateLeaveColumn = "LeaveTaken";
    public const string AllowedExtension = ".xlsx";
}

/// <summary>Machine-readable codes private to the Leave module (the generic ones are in <c>ApiErrorCodes</c>).</summary>
public static class LeaveErrorCodes
{
    /// <summary>The upload was valid but applying it failed or was rolled back. Mapped to HTTP 500.</summary>
    public const string ProcessingFailed = "LEAVE_PROCESSING_FAILED";

    /// <summary>The outcome of the transaction could not be confirmed. Mapped to HTTP 500; the admin must check the batch before retrying.</summary>
    public const string OutcomeUnknown = "LEAVE_OUTCOME_UNKNOWN";
}

/// <summary>Client-safe messages of the Leave module.</summary>
public static class LeaveMessages
{
    public const string IdentityMissing = "The signed-in identity could not be determined.";
    public const string PersonnelNotFound = "No active personnel record exists for this NSG number.";
    public const string RecordNotFound = "No leave record exists for this NSG number for the current year.";
    public const string NoLeaveRecordForYear = "No leave record has been entered for you for {0} yet.";
    public const string BatchNotFound = "The leave upload batch was not found.";
    public const string NoBatchesFound = "No leave uploads found.";
    public const string NoErrorsFound = "This leave upload has no recorded errors.";
    public const string NoHistoryFound = "No leave changes found for this NSG number.";
    public const string UploadInProgress = "Another leave upload is already being processed. Wait for it to finish, then upload again.";
    public const string FileRequired = "An Excel file (.xlsx) is required.";
    public const string UnsupportedFile = "Only Excel workbooks in the .xlsx format are accepted.";
    public const string RecordChangedConcurrently = "The leave record was changed by someone else at the same time. Please try again.";
}
