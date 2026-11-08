using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DigitalLibrary;

namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// An uploaded document that has been validated and staged on disk but is not yet visible. The caller commits it, saves the database row,
/// then completes it; disposing without completing removes the committed file again, so a failed save leaves no orphan.
/// </summary>
public interface IDigitalLibraryStagedFile : IAsyncDisposable
{
    /// <summary>Final location relative to the <c>DigitalLibrary</c> storage folder (what the database stores). Never returned to clients.</summary>
    string RelativePath { get; }

    /// <summary>Moves the staged file to its final, unique name (never overwrites).</summary>
    void Commit();

    /// <summary>Keeps the committed file. Call only after the database row has been saved.</summary>
    void Complete();
}

/// <summary>
/// Local disk storage of Digital Library files in the <c>DigitalLibrary</c> folder of the common storage root (<c>Storage:RootPath</c>):
/// <c>yyyy/MM/{guid}.{ext}</c>. Staged in <c>_incoming</c> on the same volume; every path is resolved and confined by <c>IStorageRoot</c>.
/// </summary>
public interface IDigitalLibraryStorageService
{
    /// <summary>Streams the upload to a staging file, refusing an empty file, anything over <paramref name="maxBytes"/> and anything whose first bytes are not the signature of <paramref name="type"/>.</summary>
    Task<Result<IDigitalLibraryStagedFile>> StageAsync(DigitalLibraryFileType type, Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>Opens a stored file, or null when it is missing. Throws <see cref="InvalidOperationException"/> if the relative path is unsafe.</summary>
    Stream? OpenRead(string relativePath);

    /// <summary>Deletes a stored file (used to drop a replaced file). A failure is logged and reported as false; absent counts as true.</summary>
    bool TryDelete(string relativePath);
}
