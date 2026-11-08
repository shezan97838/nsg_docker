namespace NSG.Mobile.Application.SalarySlips;

/// <summary>
/// Tunables of the Salary Slip module. Built once at startup by the composition root from the <c>SalarySlips</c> configuration
/// section (Options pattern); services never read configuration directly.
/// </summary>
public class SalarySlipSettings
{
    /// <summary>Largest accepted upload, in bytes (default 100 MB). The upload is streamed to disk, never held in memory.</summary>
    public long MaxPdfSizeBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Most pages one upload may carry.</summary>
    public int MaxPages { get; set; } = 5000;

    /// <summary>How long an upload waits for another upload of the same salary month to finish before it is refused.</summary>
    public int MonthLockTimeoutSeconds { get; set; } = 30;
}

/// <summary>Why a page of the upload did not end up in a salary slip. Stored only as page numbers; never with page content.</summary>
public enum SalarySlipPageIssue
{
    /// <summary>The page was matched to an account number.</summary>
    None = 0,

    /// <summary>The page has no extractable text (a scanned image); OCR is not supported.</summary>
    NoText,

    /// <summary>No readable number follows the <c>Bank A/c No</c> label.</summary>
    AccountNumberNotFound,

    /// <summary>The page carries the <c>Bank A/c No</c> label more than once with different numbers.</summary>
    AccountNumberAmbiguous,

    /// <summary>A salary slip for this account and month already exists; the existing one is never overwritten.</summary>
    AlreadyExists,

    /// <summary>The PDF for this account could not be generated.</summary>
    GenerationFailed
}
