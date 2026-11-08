namespace NSG.Mobile.Domain.Enums;

/// <summary>Lifecycle of a salary slip bulk-upload batch (stored as INT in <c>salary_slip_upload_batches.status</c>).</summary>
public enum SalarySlipBatchStatus
{
    /// <summary>The PDF is being read, split and stored.</summary>
    Processing = 0,

    /// <summary>Every page was matched to an account number and stored.</summary>
    Completed = 1,

    /// <summary>The batch finished but at least one page was unmatched or rejected (for example a duplicate account and month).</summary>
    CompletedWithErrors = 2,

    /// <summary>The file could not be processed (invalid PDF, no extractable text, storage failure). No salary slip was kept.</summary>
    Failed = 3
}
