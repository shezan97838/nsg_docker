using System.Globalization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Digital Library files on local persistent disk, in the <see cref="StorageModule.DigitalLibrary"/> folder of the common storage root
/// (<c>Storage:RootPath</c>): <c>yyyy/MM/{guid}.{ext}</c>. Names are server generated; the client's file name is never used. The file's first bytes must
/// match the declared type (PDF, JPEG or PNG signature). Uploads are staged in <c>_incoming</c> on the same volume so the final step is a rename that
/// never overwrites. The database stores only the relative path.
/// </summary>
public class DigitalLibraryStorageService : IDigitalLibraryStorageService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly IStorageRoot _root;
    private readonly ILogger<DigitalLibraryStorageService> _logger;

    public DigitalLibraryStorageService(IStorageRoot root, ILogger<DigitalLibraryStorageService> logger)
    {
        _root = root;
        _logger = logger;
    }

    public async Task<Result<IDigitalLibraryStagedFile>> StageAsync(DigitalLibraryFileType type, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        (byte[] signature, string refusal) = type switch
        {
            DigitalLibraryFileType.Pdf => (PdfSignature, "The file is not a PDF document."),
            DigitalLibraryFileType.Png => (PngSignature, "The file is not a PNG image."),
            _ => (JpegSignature, "The file is not a JPEG image.")
        };

        string relative = string.Create(CultureInfo.InvariantCulture,
            $"{IndianStandardTime.Now:yyyy'/'MM}/{Guid.NewGuid()}{DigitalLibraryRules.Extension(type)}");
        string target = _root.Resolve(StorageModule.DigitalLibrary, relative);
        string staged = StorageFiles.NewStagedPath(_root.IncomingFolder(StorageModule.DigitalLibrary));

        string? failure;
        try
        {
            (_, failure) = await StorageFiles.WriteStagedAsync(staged, content, maxBytes, signature, refusal, cancellationToken);
        }
        catch
        {
            StorageFiles.DeleteQuietly(staged, _logger, "A staged Digital Library upload");
            throw;
        }

        if (failure != null)
        {
            StorageFiles.DeleteQuietly(staged, _logger, "A staged Digital Library upload");
            return Result.Failure<IDigitalLibraryStagedFile>(ApiErrorCodes.ValidationFailed, failure);
        }

        return Result.Success<IDigitalLibraryStagedFile>(new StagedFile(this, relative, staged, target));
    }

    public Stream? OpenRead(string relativePath)
    {
        string path = _root.Resolve(StorageModule.DigitalLibrary, relativePath);
        // FileShare.Delete lets a concurrent clean-up remove the file while a download is still streaming it (Windows would refuse otherwise).
        return File.Exists(path)
            ? new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
    }

    public bool TryDelete(string relativePath)
    {
        try
        {
            string path = _root.Resolve(StorageModule.DigitalLibrary, relativePath);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "A Digital Library file could not be deleted");
            return false;
        }
    }

    /// <summary>Stage → Commit (rename into place, never overwriting) → Complete. Disposing before Complete removes whatever was committed or staged.</summary>
    private sealed class StagedFile : IDigitalLibraryStagedFile
    {
        private readonly DigitalLibraryStorageService _owner;
        private readonly string _stagedPath;
        private readonly string _targetPath;
        private bool _committed;
        private bool _completed;

        public StagedFile(DigitalLibraryStorageService owner, string relativePath, string stagedPath, string targetPath)
        {
            _owner = owner;
            RelativePath = relativePath;
            _stagedPath = stagedPath;
            _targetPath = targetPath;
        }

        public string RelativePath { get; }

        public void Commit()
        {
            if (_committed) throw new InvalidOperationException("The file has already been committed.");
            StorageFiles.MoveNew(_stagedPath, _targetPath);
            _committed = true;
        }

        public void Complete()
        {
            if (!_committed) throw new InvalidOperationException("The file has not been committed.");
            _completed = true;
        }

        public ValueTask DisposeAsync()
        {
            if (!_completed)
                StorageFiles.DeleteQuietly(_committed ? _targetPath : _stagedPath, _owner._logger, "A Digital Library file");
            return ValueTask.CompletedTask;
        }
    }
}
