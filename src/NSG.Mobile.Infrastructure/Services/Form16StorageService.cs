using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Form 16 PDFs on local persistent disk, in the <see cref="StorageModule.Form16"/> folder of the common storage root
/// (<c>Storage:RootPath</c>): <c>{assessmentYear}/{nsgNumber}/part-a.pdf</c> (and <c>part-b.pdf</c>). Uploads are staged in <c>_incoming</c> on the
/// same volume so the final move is a rename. The database stores only the relative path; every path is resolved and confined by <see cref="IStorageRoot"/>.
/// </summary>
public class Form16StorageService : IForm16StorageService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private readonly IStorageRoot _root;
    private readonly ILogger<Form16StorageService> _logger;

    public Form16StorageService(IStorageRoot root, ILogger<Form16StorageService> logger)
    {
        _root = root;
        _logger = logger;
    }

    public async Task<Result<IForm16StagedFile>> StageAsync(string assessmentYear, string nsgNumber, Form16Part part, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        if (Form16Rules.NormalizeAssessmentYear(assessmentYear) != assessmentYear || !Form16Rules.IsValidNsgNumber(nsgNumber))
            return Result.Failure<IForm16StagedFile>(ApiErrorCodes.ValidationFailed, "The NSG number or assessment year cannot be used as a storage location.");

        // The folder name is an encoding of the NSG number (never the raw text), so no input can add a path segment.
        string relative = $"{assessmentYear}/{Form16Rules.FolderNameFor(nsgNumber)}/{Form16Rules.FileName(part)}";
        string target = _root.Resolve(StorageModule.Form16, relative);
        string staged = StorageFiles.NewStagedPath(_root.IncomingFolder(StorageModule.Form16));
        StorageFiles.EnsureDirectory(Path.GetDirectoryName(target)!);

        string? refusal;
        try
        {
            (_, refusal) = await StorageFiles.WriteStagedAsync(staged, content, maxBytes, PdfSignature, "The file is not a PDF document.", cancellationToken);
        }
        catch
        {
            DeleteQuietly(staged);
            throw;
        }

        if (refusal != null)
        {
            DeleteQuietly(staged);
            return Result.Failure<IForm16StagedFile>(ApiErrorCodes.ValidationFailed, refusal);
        }

        return Result.Success<IForm16StagedFile>(new StagedFile(this, relative, staged, target));
    }

    public bool Exists(string relativePath) => File.Exists(_root.Resolve(StorageModule.Form16, relativePath));

    public Stream? OpenRead(string relativePath)
    {
        string path = _root.Resolve(StorageModule.Form16, relativePath);
        // FileShare.Delete lets a concurrent replacement rename the file while a download is still streaming it (Windows would refuse otherwise).
        return File.Exists(path)
            ? new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
    }

    private void DeleteQuietly(string path) => StorageFiles.DeleteQuietly(path, _logger, "A Form 16 file");

    /// <summary>Stage → Commit (previous file kept as backup) → Complete (backup dropped). Dispose before Complete restores the previous state.</summary>
    private sealed class StagedFile : IForm16StagedFile
    {
        private readonly Form16StorageService _owner;
        private readonly string _stagedPath;
        private readonly string _targetPath;
        private string? _backupPath;
        private bool _committed;
        private bool _completed;

        public StagedFile(Form16StorageService owner, string relativePath, string stagedPath, string targetPath)
        {
            _owner = owner;
            RelativePath = relativePath;
            _stagedPath = stagedPath;
            _targetPath = targetPath;
        }

        public string RelativePath { get; }

        public void Commit()
        {
            if (_committed) throw new InvalidOperationException("The Form 16 file has already been committed.");

            if (File.Exists(_targetPath))
            {
                _backupPath = _targetPath + "." + Guid.NewGuid().ToString("N") + ".bak";
                File.Move(_targetPath, _backupPath);
            }

            try
            {
                File.Move(_stagedPath, _targetPath);
            }
            catch
            {
                RestoreBackup();
                throw;
            }

            _committed = true;
        }

        public void Complete()
        {
            if (!_committed) throw new InvalidOperationException("The Form 16 file has not been committed.");
            _completed = true;
            if (_backupPath != null) _owner.DeleteQuietly(_backupPath);
            _backupPath = null;
        }

        public ValueTask DisposeAsync()
        {
            if (_completed) return ValueTask.CompletedTask;

            try
            {
                if (_committed)
                {
                    // The row was not saved: remove the new file and put the old one back (or leave nothing if there was none).
                    _owner.DeleteQuietly(_targetPath);
                    RestoreBackup();
                }
                else
                {
                    _owner.DeleteQuietly(_stagedPath);
                }
            }
            catch (Exception ex)
            {
                _owner._logger.LogError(ex, "A Form 16 file could not be rolled back");
            }

            return ValueTask.CompletedTask;
        }

        private void RestoreBackup()
        {
            if (_backupPath != null && File.Exists(_backupPath)) File.Move(_backupPath, _targetPath, overwrite: true);
            _backupPath = null;
        }
    }
}
