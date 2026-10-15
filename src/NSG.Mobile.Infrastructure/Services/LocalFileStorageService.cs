using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using Microsoft.Extensions.Options;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Local filesystem-based file storage for development and single-server deployment.
/// Files are stored outside the web root with server-generated names.
/// For production multi-server deployment, consider NFS/cloud storage.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<LocalFileStorageService> _logger;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase);

    public LocalFileStorageService(IOptions<FileStorageOptions> options, ILogger<LocalFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        foreach (var ext in _options.AllowedExtensions)
            AllowedExtensions.Add(ext);
    }

    public async Task<string> StoreAsync(Stream fileStream, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        // Validate extension
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"File extension '{ext}' is not allowed.");

        // Validate file size
        if (fileStream.Length > _options.MaxFileSizeBytes)
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_options.MaxFileSizeBytes} bytes.");

        // Generate server-side file name — never trust client-provided names
        var storageKey = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{category}/{NSG.Mobile.Domain.Common.IndianStandardTime.Now:yyyy'/'MM}/{Guid.NewGuid()}{ext}");   // literal slashes: "/" in a date format is culture-dependent
        var fullPath = Path.Combine(_options.BasePath, storageKey);

        // Prevent path traversal
        var resolvedPath = Path.GetFullPath(fullPath);
        var basePath = Path.GetFullPath(_options.BasePath);
        if (!IsInside(basePath, resolvedPath))
            throw new InvalidOperationException("Path traversal detected.");

        // Create the directory only after the target is known to be inside the storage root.
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        await using var fileStreamOut = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fileStream.CopyToAsync(fileStreamOut, cancellationToken);

        _logger.LogInformation("File stored: {StorageKey}, ContentType: {ContentType}, Size: {Size}",
            storageKey, contentType, fileStream.Length);

        return storageKey;
    }

    public Task<FileStorageResult?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_options.BasePath, storageKey);
        var resolvedPath = Path.GetFullPath(fullPath);
        var basePath = Path.GetFullPath(_options.BasePath);

        if (!IsInside(basePath, resolvedPath))
            return Task.FromResult<FileStorageResult?>(null);

        if (!File.Exists(resolvedPath))
            return Task.FromResult<FileStorageResult?>(null);

        var ext = Path.GetExtension(storageKey).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = Path.GetFileName(storageKey);

        return Task.FromResult<FileStorageResult?>(new FileStorageResult(stream, contentType, fileName));
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_options.BasePath, storageKey);
        var resolvedPath = Path.GetFullPath(fullPath);
        var basePath = Path.GetFullPath(_options.BasePath);

        if (!IsInside(basePath, resolvedPath))
            return Task.FromResult(false);

        if (File.Exists(resolvedPath))
        {
            File.Delete(resolvedPath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// True when <paramref name="resolvedPath"/> lies inside <paramref name="basePath"/>. Compares whole path segments (so
    /// "/data/files-evil" is not inside "/data/files") and honours the file system: case-insensitive on Windows, case-sensitive on Linux.
    /// </summary>
    public static bool IsInside(string basePath, string resolvedPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string root = Path.TrimEndingDirectorySeparator(basePath) + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(root, comparison);
    }
}
