using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.SalarySlips;

namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>What was found on one page of the uploaded PDF. Holds only the account number, never the page text.</summary>
public sealed record SalarySlipPageInfo(int PageNumber, string? AccountNumber, SalarySlipPageIssue Issue);

/// <summary>Page-wise text extraction with a local library (no OCR, no cloud service).</summary>
public interface ISalarySlipPdfExtractionService
{
    /// <summary>
    /// Reads every page independently and returns, in page order, the <c>Bank A/c No</c> of each. Fails with a client-safe message
    /// when the file is not a readable PDF or has more than <paramref name="maxPages"/> pages.
    /// </summary>
    Task<Result<IReadOnlyList<SalarySlipPageInfo>>> ExtractAsync(string pdfPath, int maxPages, CancellationToken cancellationToken);
}

/// <summary>Builds the per-account PDFs by copying the ORIGINAL pages (no re-rendering, so layout and quality are unchanged).</summary>
public interface ISalarySlipPdfGenerationService
{
    /// <summary>Opens the uploaded PDF once; every account's PDF is then built from it. Throws <see cref="InvalidDataException"/> for an unreadable file.</summary>
    Task<ISalarySlipPdfSource> OpenSourceAsync(string pdfPath, CancellationToken cancellationToken);
}

/// <summary>An opened source PDF. Dispose it as soon as all accounts are written.</summary>
public interface ISalarySlipPdfSource : IDisposable
{
    int PageCount { get; }

    /// <summary>Writes a new PDF holding the given 1-based pages (in the order given) to <paramref name="destinationPath"/>, which must not exist.</summary>
    Task WritePagesAsync(IReadOnlyList<int> pageNumbers, string destinationPath, CancellationToken cancellationToken);
}

/// <summary>Where a salary slip PDF lives. <see cref="RelativePath"/> is what the database stores; <see cref="AbsolutePath"/> never leaves the server.</summary>
public sealed record SalarySlipFileLocation(string RelativePath, string AbsolutePath);

/// <summary>The uploaded PDF on local disk. Disposing deletes it.</summary>
public sealed class SalarySlipIncomingFile : IAsyncDisposable
{
    private readonly Func<ValueTask> _cleanup;

    public SalarySlipIncomingFile(string path, long length, Func<ValueTask> cleanup)
    {
        Path = path;
        Length = length;
        _cleanup = cleanup;
    }

    public string Path { get; }
    public long Length { get; }

    public ValueTask DisposeAsync() => _cleanup();
}

/// <summary>
/// Local persistent disk storage for salary slip PDFs on the configured root (outside the web root). Layout:
/// <c>{root}/salary-slips/{yyyy}/{MM}/{guid}.pdf</c>, with uploads staged in <c>{root}/salary-slips/_incoming</c>.
/// Names are server generated and carry no personal data.
/// </summary>
public interface ISalarySlipStorageService
{
    /// <summary>Streams the upload to a staging file, refusing anything over <paramref name="maxBytes"/> or not starting with the PDF signature.</summary>
    Task<Result<SalarySlipIncomingFile>> SaveIncomingAsync(Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>Reserves a new unique location for the slip of <paramref name="salaryMonth"/> and creates its directory. The file itself is created by the caller.</summary>
    SalarySlipFileLocation ReserveSlipLocation(DateOnly salaryMonth);

    /// <summary>Deletes a stored slip by its location; a missing file is not an error.</summary>
    Task DeleteAsync(SalarySlipFileLocation location, CancellationToken cancellationToken);

    /// <summary>Whether the stored file exists. Throws <see cref="InvalidOperationException"/> if the relative path escapes the storage root.</summary>
    bool Exists(string relativePath);

    /// <summary>Opens a stored file for reading, or null when it is missing. Throws <see cref="InvalidOperationException"/> if the relative path escapes the storage root.</summary>
    Stream? OpenRead(string relativePath);
}
