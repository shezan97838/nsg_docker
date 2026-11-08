using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// <see cref="IFileStorageService"/> on the local file system: the object store for grievance documents, DG message images and banner images.
/// Keys are server generated (<c>{category}/{yyyy}/{MM}/{guid}{ext}</c>, the client's file name is never part of a key) and are what the database
/// keeps. With the common storage root (<c>Storage:RootPath</c>) the category selects the module folder
/// (<c>grievances</c> → <c>Grievance</c>, <c>dg-messages</c> → <c>DgMessages</c>, <c>banners</c> → <c>Banners</c>) and is not repeated on disk:
/// <c>grievances/2026/11/{guid}.pdf</c> lives at <c>Grievance/2026/11/{guid}.pdf</c>. Writes are staged and renamed, never overwriting. No public or
/// presigned URL is ever produced; callers stream files through the API after their own authorization check.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private static readonly Regex ExtensionPattern = new(@"^\.[a-z0-9]{1,10}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IStorageRoot _root;
    private readonly FileStorageOptions _limits;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly HashSet<string> _allowedExtensions;

    public LocalFileStorageService(IStorageRoot root, IOptions<FileStorageOptions> limits, ILogger<LocalFileStorageService> logger)
    {
        _root = root;
        _limits = limits.Value;
        _logger = logger;
        _allowedExtensions = new HashSet<string>(_limits.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> StoreAsync(Stream fileStream, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ExtensionPattern.IsMatch(extension) || !_allowedExtensions.Contains(extension))
            throw new InvalidOperationException($"File extension '{extension}' is not allowed.");
        StorageModule module = StorageModules.FromCategory(category) ?? throw new InvalidOperationException("Storage category is not valid.");
        if (fileStream.CanSeek && fileStream.Length - fileStream.Position > _limits.MaxFileSizeBytes)
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_limits.MaxFileSizeBytes} bytes.");

        // Server-side name only; "/" is a literal in the format because the date format's "/" is culture dependent.
        string key = string.Create(CultureInfo.InvariantCulture,
            $"{category}/{NSG.Mobile.Domain.Common.IndianStandardTime.Now:yyyy'/'MM}/{Guid.NewGuid()}{extension}");

        string? staged = null;
        try
        {
            string target = _root.Resolve(module, key);
            staged = StorageFiles.NewStagedPath(_root.IncomingFolder(module));

            (long size, string? refusal) = await StorageFiles.WriteStagedAsync(staged, fileStream, _limits.MaxFileSizeBytes, null, string.Empty, cancellationToken);
            if (refusal != null)
                throw new InvalidOperationException(refusal.Contains("larger", StringComparison.Ordinal)
                    ? $"File size exceeds maximum allowed size of {_limits.MaxFileSizeBytes} bytes."
                    : refusal);

            StorageFiles.MoveNew(staged, target);
            staged = null;
            _logger.LogInformation("File stored: {StorageKey}, ContentType: {ContentType}, Size: {Size}", key, contentType, size);
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Local storage write failed for {StorageKey}", key);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
        finally
        {
            if (staged != null) StorageFiles.DeleteQuietly(staged, _logger, "A staged upload");
        }
    }

    public Task<FileStorageResult?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(storageKey, out string? path) || !File.Exists(path))
            return Task.FromResult<FileStorageResult?>(null);

        try
        {
            FileStream stream = new(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<FileStorageResult?>(new FileStorageResult(stream, ContentTypeFor(storageKey), Path.GetFileName(storageKey)));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<FileStorageResult?>(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Local storage read failed for {StorageKey}", storageKey);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
    }

    /// <summary>Deletes the file. Used as a compensating action, so a failure is logged and reported as false instead of thrown. Like object stores, true also means "already absent".</summary>
    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(storageKey, out string? path)) return Task.FromResult(false);

        try
        {
            if (File.Exists(path)) File.Delete(path);
            _logger.LogInformation("File deleted: {StorageKey}", storageKey);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Local storage delete failed for {StorageKey}; the file may be orphaned", storageKey);
            return Task.FromResult(false);
        }
    }

    /// <summary>True only for keys of the exact shape <see cref="StoreAsync"/> generates; anything else (traversal, absolute paths, other prefixes) is refused.</summary>
    public static bool IsValidKey(string? storageKey) => StorageModules.IsValidObjectKey(storageKey);

    private bool TryResolve(string storageKey, out string path)
    {
        path = string.Empty;
        if (!IsValidKey(storageKey) || StorageModules.FromCategory(storageKey[..storageKey.IndexOf('/')]) is not StorageModule module)
            return false;

        try
        {
            path = _root.Resolve(module, storageKey);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;   // a link or otherwise unsafe location: treated as not found
        }
    }

    private static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xml" => "application/xml",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };
}
