using Microsoft.Extensions.Options;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Services.Storage;

/// <summary>
/// The single place that turns configuration into physical locations. Every storage service (Form 16, salary slips, and the
/// local grievance / DG message / banner object store) asks it for a module folder or resolves a stored path through it, so the
/// path-safety rules exist exactly once. There is exactly one root, <c>Storage:RootPath</c>; there is no per-module root and no fallback to one.
/// </summary>
public interface IStorageRoot
{
    /// <summary>True when <c>Storage:RootPath</c> holds a non-blank value. It says nothing about whether the value is usable; <see cref="Validate"/> does.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Checks <c>Storage:RootPath</c>: configured, absolute, existing, writable, outside the web root and the deployment directory, no links
    /// in the module folders; creates the module folders (never the root itself). Throws <see cref="InvalidOperationException"/> naming the
    /// setting (never a secret).
    /// </summary>
    void Validate();

    /// <summary>Absolute folder of one module. Throws when the root is not configured or not usable.</summary>
    string ModuleRoot(StorageModule module);

    /// <summary>Absolute staging folder on the same volume as the module's files, so the final step is a rename.</summary>
    string IncomingFolder(StorageModule module);

    /// <summary>
    /// Absolute path of a stored database value (relative path or object key), guaranteed to be inside the module's folder.
    /// Throws <see cref="InvalidOperationException"/> for anything that is not a plain relative path of that module.
    /// </summary>
    string Resolve(StorageModule module, string storedPath);
}

/// <inheritdoc />
public sealed class StorageRoot : IStorageRoot
{
    private const int MaxStoredPathLength = 500;     // the widest database column that holds one
    private const int MaxSegmentLength = 255;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly StorageOptions _storage;
    private readonly object _gate = new();
    private string? _centralRoot;

    public StorageRoot(IOptions<StorageOptions> storage) => _storage = storage.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_storage.RootPath);

    public void Validate() => CentralRoot();

    public string ModuleRoot(StorageModule module) => Path.Combine(CentralRoot(), StorageModules.FolderName(module));

    public string IncomingFolder(StorageModule module) => Path.Combine(CentralRoot(), StorageModules.IncomingFolderName);

    public string Resolve(StorageModule module, string storedPath)
    {
        string[] segments = SplitStoredPath(storedPath);
        string moduleRoot = ModuleRoot(module);

        // The layout drops the segment that only identifies the module inside a shared namespace (salary-slips/, grievances/ …).
        if (StorageModules.Category(module) is string category)
        {
            if (segments.Length < 2 || !string.Equals(segments[0], category, StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid storage path.");
            segments = segments[1..];
        }

        string full = Path.GetFullPath(Path.Combine(moduleRoot, Path.Combine(segments)));
        if (!IsInside(moduleRoot, full))
            throw new InvalidOperationException("Invalid storage path.");

        EnsureNoLinks(CentralRoot(), full);
        return full;
    }

    /// <summary>
    /// True when <paramref name="resolvedPath"/> lies inside <paramref name="basePath"/>. Compares whole path segments (so
    /// "/data/files-evil" is not inside "/data/files") and honours the file system: case-insensitive on Windows, case-sensitive on Linux.
    /// </summary>
    public static bool IsInside(string basePath, string resolvedPath)
    {
        string root = Path.TrimEndingDirectorySeparator(basePath) + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(root, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // ───── the root ─────


    private string CentralRoot()
    {
        if (_centralRoot != null) return _centralRoot;
        lock (_gate)
        {
            // A failed validation is not cached, so fixing the folder and retrying works without a restart.
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "Storage:RootPath is not configured. It is required whenever documents are stored on the local file system "
                    + "(set Storage__RootPath, for example D:\\NSGStorage on Windows or /var/lib/nsg-mobile/uploads on Linux). There is no fallback to any other storage root.");
            _centralRoot ??= PrepareCentralRoot(_storage.RootPath.Trim());
            return _centralRoot;
        }
    }

    private static string PrepareCentralRoot(string configured)
    {
        const string key = "Storage:RootPath";
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException($"{key} must be an absolute path (for example D:\\NSGStorage on Windows or /var/lib/nsg-mobile/uploads on Linux).");
        if (ContainsWebRootSegment(configured))
            throw new InvalidOperationException($"{key} must be outside the web root.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        if (string.Equals(root, Path.GetPathRoot(root), PathComparison))
            throw new InvalidOperationException($"{key} must be a folder, not a drive or the file-system root.");

        // Outside the deployment directory too: a redeploy replaces that directory and would take the documents with it.
        string deployment = Path.GetFullPath(AppContext.BaseDirectory);
        if (IsInside(deployment, root) || string.Equals(Path.TrimEndingDirectorySeparator(deployment), root, PathComparison))
            throw new InvalidOperationException($"{key} must be outside the application deployment directory.");

        if (!Directory.Exists(root))
            throw new InvalidOperationException($"{key} does not exist: create the folder (and, on Linux, give the service account write access) before starting the application.");

        foreach (StorageModule module in StorageModules.All)
            EnsureModuleFolder(root, StorageModules.FolderName(module));
        EnsureModuleFolder(root, StorageModules.IncomingFolderName);
        ProbeWritable(root, key);
        return root;
    }

    private static void EnsureModuleFolder(string root, string name)
    {
        string path = Path.Combine(root, name);
        if (File.Exists(path))
            throw new InvalidOperationException($"Storage:RootPath contains a file where the folder '{name}' must be.");
        if (Directory.Exists(path) && IsLink(new DirectoryInfo(path)))
            throw new InvalidOperationException($"Storage folder '{name}' is a symbolic link or junction, which is not allowed.");
        StorageFiles.EnsureDirectory(path);
    }

    private static void ProbeWritable(string root, string key)
    {
        string probe = Path.Combine(root, StorageModules.IncomingFolderName, ".write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (FileStream stream = new(probe, StorageFiles.CreateOptions()))
                stream.WriteByte(0);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{key} is not writable by the application account.", ex);
        }
    }

    // ───── shared checks ─────

    private static bool ContainsWebRootSegment(string path)
        => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Any(s => s.Equals("wwwroot", StringComparison.OrdinalIgnoreCase));

    /// <summary>Validates a stored value as a plain forward-slash relative path and returns its segments.</summary>
    private static string[] SplitStoredPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || storedPath.Length > MaxStoredPathLength || storedPath[0] == '/' || Path.IsPathRooted(storedPath))
            throw new InvalidOperationException("Invalid storage path.");

        string[] segments = storedPath.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment.Length > MaxSegmentLength || segment is "." or ".."
                || segment[^1] is '.' or ' ' || segment[0] == ' '
                || segment.Any(c => char.IsControl(c) || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                || WindowsReservedNames.Contains(segment.Split('.')[0]))
                throw new InvalidOperationException("Invalid storage path.");
        }

        return segments;
    }

    /// <summary>Refuses a path that passes through an existing symbolic link or junction below <paramref name="baseDirectory"/>.</summary>
    private static void EnsureNoLinks(string baseDirectory, string full)
    {
        string current = baseDirectory;
        foreach (string segment in Path.GetRelativePath(baseDirectory, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists) return;   // nothing deeper exists yet, so nothing deeper can be a link
            if (IsLink(info)) throw new InvalidOperationException("Invalid storage path.");
        }
    }

    private static bool IsLink(FileSystemInfo info) => info.LinkTarget != null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
