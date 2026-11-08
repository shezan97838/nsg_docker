using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.DigitalLibrary;

/// <summary>
/// Tunables of the Digital Library. Built once at startup from the <c>DigitalLibrary</c> configuration section (Options pattern).
/// Limits only: documents live under the common storage root (<c>Storage:RootPath</c>, folder <c>DigitalLibrary</c>).
/// </summary>
public class DigitalLibrarySettings
{
    /// <summary>Largest accepted file in bytes (default 10 MB, the same ceiling as Form 16 PDFs). The upload is streamed to disk, never held in memory.</summary>
    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024;
}

/// <summary>The accepted file types. Anything else (DOC, XLS, ZIP, MP4, executables ...) is refused.</summary>
public enum DigitalLibraryFileType
{
    Pdf,
    Jpg,
    Jpeg,
    Png
}

/// <summary>Constants and helpers of the Digital Library.</summary>
public static class DigitalLibraryRules
{
    public const int DescriptionMaxLength = 1000;
    public const int MaxPageSize = 100;

    public const string NotFoundMessage = "Digital library record not found.";
    public const string FileUnavailableMessage = "The file is no longer available.";
    public const string NoRecordsMessage = "No digital library records found.";
    public const string RecordsRetrievedMessage = "Digital library records retrieved successfully.";
    public const string RecordRetrievedMessage = "Digital library record retrieved successfully.";
    public const string CreatedMessage = "Digital library record created successfully.";
    public const string UpdatedMessage = "Digital library record updated successfully.";
    public const string DeactivatedMessage = "Digital library record deactivated successfully.";
    public const string AlreadyInactiveMessage = "Digital library record is already inactive.";
    public const string StorageFailureMessage = "The file could not be stored. Please try again.";
    public const string UnsupportedTypeMessage = "Only PDF, JPG, JPEG and PNG files are accepted.";

    /// <summary>Relative API route that streams the file to mobile users (they call it with their bearer token).</summary>
    public static string MobileFilePath(long id) => $"/api/v1/digital-library/{id}/file";

    /// <summary>The type named by the file name's extension, or null when it is not one of the accepted types.</summary>
    public static DigitalLibraryFileType? TypeFromFileName(string? fileName)
    {
        string extension = Path.GetExtension((fileName ?? string.Empty).Replace('\\', '/')).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => DigitalLibraryFileType.Pdf,
            ".jpg" => DigitalLibraryFileType.Jpg,
            ".jpeg" => DigitalLibraryFileType.Jpeg,
            ".png" => DigitalLibraryFileType.Png,
            _ => null
        };
    }

    /// <summary>The file type stored in <c>digital_library.file_type</c>.</summary>
    public static string Label(DigitalLibraryFileType type) => type.ToString().ToUpperInvariant();

    public static string Extension(DigitalLibraryFileType type) => "." + type.ToString().ToLowerInvariant();

    /// <summary>Parses a filter value (PDF, JPG, JPEG or PNG, case-insensitive); null when it is not an accepted type.</summary>
    public static DigitalLibraryFileType? ParseLabel(string? value) => (value?.Trim().ToUpperInvariant()) switch
    {
        "PDF" => DigitalLibraryFileType.Pdf,
        "JPG" => DigitalLibraryFileType.Jpg,
        "JPEG" => DigitalLibraryFileType.Jpeg,
        "PNG" => DigitalLibraryFileType.Png,
        _ => null
    };

    /// <summary>Content type of a stored file, derived from the validated <c>file_type</c> column, never from the client.</summary>
    public static string ContentType(string fileTypeLabel) => ParseLabel(fileTypeLabel) switch
    {
        DigitalLibraryFileType.Pdf => "application/pdf",
        DigitalLibraryFileType.Png => "image/png",
        _ => "image/jpeg"
    };
}

/// <summary>One uploaded file. The caller owns and disposes <see cref="Content"/>.</summary>
public sealed record DigitalLibraryUploadFile(Stream Content, string FileName, long Length);

/// <summary>Create request. <see cref="IsImportant"/> defaults to false.</summary>
public sealed record DigitalLibraryCreateRequest(string? Description, bool? IsImportant, DigitalLibraryUploadFile? File);

/// <summary>Update request. <see cref="IsImportant"/> is required; <see cref="File"/> is optional (the existing file is kept without it).</summary>
public sealed record DigitalLibraryUpdateRequest(string? Description, bool? IsImportant, DigitalLibraryUploadFile? File);

/// <summary>Admin listing filters. All optional.</summary>
public sealed record DigitalLibraryAdminFilter(string? Search, bool? IsActive, bool? IsImportant, string? FileType);

/// <summary>A record as the Admin Portal sees it. No storage path.</summary>
public sealed class DigitalLibraryAdminDto
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>A record as the mobile app sees it. <see cref="FileUrl"/> is a relative API route, never a storage path.</summary>
public sealed class DigitalLibraryMobileDto
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An opened file. The caller owns (and must dispose) <see cref="Content"/>.</summary>
public sealed record DigitalLibraryDownload(Stream Content, string ContentType, string FileName);

internal static class DigitalLibraryMapping
{
    public static DigitalLibraryAdminDto ToAdminDto(this DigitalLibraryItem i) => new()
    {
        Id = i.Id,
        Description = i.Description,
        FileType = i.FileType,
        IsImportant = i.IsImportant,
        IsActive = i.IsActive,
        CreatedAt = IstTime.FromIst(i.CreatedAtIst),
        CreatedBy = i.CreatedBy,
        UpdatedAt = i.UpdatedAtIst == null ? null : IstTime.FromIst(i.UpdatedAtIst.Value),
        UpdatedBy = i.UpdatedBy
    };

    public static DigitalLibraryMobileDto ToMobileDto(this DigitalLibraryItem i) => new()
    {
        Id = i.Id,
        Description = i.Description,
        IsImportant = i.IsImportant,
        FileType = i.FileType,
        FileUrl = DigitalLibraryRules.MobileFilePath(i.Id),
        CreatedAt = IstTime.FromIst(i.CreatedAtIst)
    };
}
