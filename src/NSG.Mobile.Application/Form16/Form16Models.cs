namespace NSG.Mobile.Application.Form16;

/// <summary>
/// Tunables of the Form 16 module. Built once at startup by the composition root from the <c>Form16Storage</c> configuration
/// section (Options pattern); services never read configuration directly.
/// </summary>
public class Form16Settings
{
    /// <summary>Largest accepted PDF, in bytes (default 10 MB). The upload is streamed to disk, never held in memory.</summary>
    public long MaxPdfSizeBytes { get; set; } = 10L * 1024 * 1024;
}

/// <summary>One uploaded PDF. The caller owns and disposes <see cref="Content"/>.</summary>
public sealed record Form16UploadFile(Stream Content, long Length);

/// <summary>The upload of one or both parts for one person and assessment year. At least one part is required.</summary>
public sealed record Form16UploadRequest(string? NsgNumber, string? AssessmentYear, Form16UploadFile? PartA, Form16UploadFile? PartB);

/// <summary>A Form 16 record as the Admin Portal sees it. No storage path.</summary>
public sealed class Form16AdminDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Name from the personnel record; null if the personnel record no longer exists.</summary>
    public string? EmployeeName { get; set; }

    public string AssessmentYear { get; set; } = string.Empty;
    public bool PartAAvailable { get; set; }
    public DateTimeOffset? PartAUploadedAt { get; set; }
    public bool PartBAvailable { get; set; }
    public DateTimeOffset? PartBUploadedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Outcome of a POST: whether a new record was created or an existing one updated, with the message to send.</summary>
public sealed record Form16SaveOutcome(bool Created, string Message, Form16AdminDto Record);

/// <summary>One assessment year of the signed-in person's Form 16. Availability and timestamps only; no storage path.</summary>
public sealed class Form16MobileDto
{
    public string AssessmentYear { get; set; } = string.Empty;
    public bool PartAAvailable { get; set; }
    public DateTimeOffset? PartAUploadedAt { get; set; }
    public bool PartBAvailable { get; set; }
    public DateTimeOffset? PartBUploadedAt { get; set; }

    /// <summary>Relative API route of Part A; null when Part A is not available.</summary>
    public string? PartADownloadUrl { get; set; }

    /// <summary>Relative API route of Part B; null when Part B is not available.</summary>
    public string? PartBDownloadUrl { get; set; }
}

/// <summary>An opened Form 16 PDF. The caller owns (and must dispose) <see cref="Content"/>.</summary>
public sealed record Form16Download(Stream Content, string FileName);
