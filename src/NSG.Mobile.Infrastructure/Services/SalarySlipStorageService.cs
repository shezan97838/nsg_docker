using System.Globalization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Salary slip PDFs on local persistent disk, in the <see cref="StorageModule.SalarySlips"/> folder of the common storage root
/// (<c>Storage:RootPath</c>). Not the shared <see cref="IFileStorageService"/>. The database keeps the value
/// <c>salary-slips/{yyyy}/{MM}/{guid}.pdf</c>; <see cref="IStorageRoot"/> drops the leading <c>salary-slips/</c> so the file lives at
/// <c>SalarySlips/{yyyy}/{MM}/{guid}.pdf</c>. Staged uploads go to the root's <c>_incoming</c> folder.
/// The database stores only the relative path; every path is checked to stay inside the module folder.
/// </summary>
public class SalarySlipStorageService : ISalarySlipStorageService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private readonly IStorageRoot _root;
    private readonly ILogger<SalarySlipStorageService> _logger;

    public SalarySlipStorageService(IStorageRoot root, ILogger<SalarySlipStorageService> logger)
    {
        _root = root;
        _logger = logger;
    }

    public async Task<Result<SalarySlipIncomingFile>> SaveIncomingAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        string path = StorageFiles.NewStagedPath(_root.IncomingFolder(StorageModule.SalarySlips));

        long total;
        string? refusal;
        try
        {
            (total, refusal) = await StorageFiles.WriteStagedAsync(path, content, maxBytes, PdfSignature, "The file is not a PDF document.", cancellationToken);
        }
        catch
        {
            DeleteQuietly(path);
            throw;
        }

        if (refusal != null)
        {
            DeleteQuietly(path);
            return Result.Failure<SalarySlipIncomingFile>(ApiErrorCodes.ValidationFailed, refusal);
        }

        return Result.Success(new SalarySlipIncomingFile(path, total, () =>
        {
            DeleteQuietly(path);
            return ValueTask.CompletedTask;
        }));
    }

    public SalarySlipFileLocation ReserveSlipLocation(DateOnly salaryMonth)
    {
        string relative = string.Create(CultureInfo.InvariantCulture, $"{StorageModules.SalarySlipPrefix}/{salaryMonth.Year:0000}/{salaryMonth.Month:00}/{Guid.NewGuid():N}.pdf");
        string absolute = _root.Resolve(StorageModule.SalarySlips, relative);
        StorageFiles.EnsureDirectory(Path.GetDirectoryName(absolute)!);
        return new SalarySlipFileLocation(relative, absolute);
    }

    public Task DeleteAsync(SalarySlipFileLocation location, CancellationToken cancellationToken)
    {
        // Resolve again from the relative path so a caller cannot make us delete outside the module folder.
        DeleteQuietly(_root.Resolve(StorageModule.SalarySlips, location.RelativePath));
        return Task.CompletedTask;
    }

    public bool Exists(string relativePath) => File.Exists(_root.Resolve(StorageModule.SalarySlips, relativePath));

    public Stream? OpenRead(string relativePath)
    {
        string path = _root.Resolve(StorageModule.SalarySlips, relativePath);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan) : null;
    }

    private void DeleteQuietly(string path) => StorageFiles.DeleteQuietly(path, _logger, "A salary slip file");
}
