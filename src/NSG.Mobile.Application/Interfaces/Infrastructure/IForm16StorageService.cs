using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;

namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// An uploaded Form 16 PDF that has been validated and staged on disk but is not yet visible. The caller commits it, saves the database row,
/// then completes it; disposing without completing puts the previous file back, so a failed database save never leaves the files and the row disagreeing.
/// </summary>
public interface IForm16StagedFile : IAsyncDisposable
{
    /// <summary>Final location relative to the Form 16 storage root (what the database stores). Never returned to clients.</summary>
    string RelativePath { get; }

    /// <summary>Moves the staged file into place. A file already there is kept as a backup until <see cref="Complete"/>.</summary>
    void Commit();

    /// <summary>Makes the replacement permanent by discarding the backup. Call only after the database row has been saved.</summary>
    void Complete();
}

/// <summary>
/// Local persistent disk storage for Form 16 PDFs (never under wwwroot or the deployment directory). Layout under
/// <c>Form16Storage:RootPath</c>: <c>{assessmentYear}/{nsgNumber}/part-a.pdf</c> and <c>part-b.pdf</c>; uploads are staged in <c>_incoming</c>.
/// </summary>
public interface IForm16StorageService
{
    /// <summary>
    /// Streams the upload to a staging file, refusing an empty file, anything over <paramref name="maxBytes"/> and anything that does not start
    /// with the PDF signature. The target folders are created as needed.
    /// </summary>
    Task<Result<IForm16StagedFile>> StageAsync(string assessmentYear, string nsgNumber, Form16Part part, Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading, or null when it is missing. Throws <see cref="InvalidOperationException"/> if the relative path escapes the storage root.</summary>
    Stream? OpenRead(string relativePath);

    /// <summary>Whether the stored file exists. Throws <see cref="InvalidOperationException"/> if the relative path escapes the storage root.</summary>
    bool Exists(string relativePath);
}
