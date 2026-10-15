namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Secure file storage abstraction.
/// SOW §3.3: Salary slip PDFs.
/// SOW §3.4: Form-16 PDFs.
/// SOW §3.5: Newsletter/circular PDFs.
/// SOW §3.6: Digital Library documents.
/// SOW §3.8: Grievance supporting documents.
/// 
/// Files must be stored outside the web root, with server-generated names.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Store a file securely. Returns the storage key (NOT the physical path).</summary>
    Task<string> StoreAsync(Stream fileStream, string fileName, string contentType, string category, CancellationToken cancellationToken = default);

    /// <summary>Retrieve a file stream by storage key. Returns null if not found.</summary>
    Task<FileStorageResult?> GetAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Delete a file by storage key.</summary>
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public record FileStorageResult(Stream Stream, string ContentType, string FileName);
