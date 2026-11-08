using Microsoft.Extensions.Logging;

namespace NSG.Mobile.Infrastructure.Services.Storage;

/// <summary>
/// The shared way to put a file on disk safely: stage under a random name with <c>CreateNew</c> (never overwriting, never visible under its
/// final name), flush to disk, then rename into place. A failure at any point leaves no partial file under the final name.
/// </summary>
public static class StorageFiles
{
    private const int BufferSize = 81920;

    // Linux: directories rwxr-x---, files rw-r----- (owner = the service account, group may read for backups).
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
    private const UnixFileMode FileMode640 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    /// <summary>Creates the folder (and its parents) with private permissions on Linux.</summary>
    public static void EnsureDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, DirectoryMode);
    }

    /// <summary>Options for a new, exclusive, write-only file.</summary>
    public static FileStreamOptions CreateOptions()
    {
        FileStreamOptions options = new()
        {
            Mode = System.IO.FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = BufferSize,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = FileMode640;
        return options;
    }

    /// <summary>A path for a new staged upload inside <paramref name="incomingFolder"/> (created if needed).</summary>
    public static string NewStagedPath(string incomingFolder)
    {
        EnsureDirectory(incomingFolder);
        return Path.Combine(incomingFolder, Guid.NewGuid().ToString("N") + ".upload");
    }

    /// <summary>
    /// Copies <paramref name="content"/> to <paramref name="stagedPath"/> (which must not exist). Returns a refusal message - and the caller
    /// must then delete the staged file - when the content is empty, larger than <paramref name="maxBytes"/>, or does not start with
    /// <paramref name="requiredSignature"/>; otherwise null. The data is flushed to disk before returning.
    /// </summary>
    public static async Task<(long Total, string? Refusal)> WriteStagedAsync(
        string stagedPath, Stream content, long maxBytes, byte[]? requiredSignature, string signatureRefusal, CancellationToken cancellationToken)
    {
        long total = 0;
        bool signatureChecked = requiredSignature == null;
        string? refusal = null;

        await using (FileStream output = new(stagedPath, CreateOptions()))
        {
            byte[] buffer = new byte[BufferSize];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (!signatureChecked)
                {
                    // The first read of a stream can be shorter than the signature; only judge a full signature's worth.
                    if (read < requiredSignature!.Length || !buffer.AsSpan(0, requiredSignature.Length).SequenceEqual(requiredSignature))
                    {
                        refusal = signatureRefusal;
                        break;
                    }

                    signatureChecked = true;
                }

                total += read;
                if (total > maxBytes)
                {
                    refusal = $"The file is larger than the {maxBytes / (1024 * 1024)} MB limit.";
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (refusal == null && total > 0) output.Flush(flushToDisk: true);
        }

        if (refusal == null && total == 0) refusal = "The file is empty.";
        return (total, refusal);
    }

    /// <summary>Renames a staged file to its final path, failing (never replacing) when something is already there.</summary>
    public static void MoveNew(string stagedPath, string targetPath)
    {
        EnsureDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(stagedPath, targetPath, overwrite: false);
    }

    /// <summary>Deletes a file if present; a failure is logged (path-free) and swallowed because this runs as cleanup.</summary>
    public static void DeleteQuietly(string path, ILogger logger, string what)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{What} could not be deleted", what);
        }
    }
}
