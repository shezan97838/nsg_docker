using NSG.Mobile.Domain.Common;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// One document (circular, notice, PDF or image) of the Digital Library. Table <c>digital_library</c>. Uploaded by a Media Cell admin and read by
/// mobile users while <see cref="IsActive"/> is true. Rows are never deleted: deleting sets <see cref="IsActive"/> to false and the file is kept.
/// </summary>
public class DigitalLibraryItem
{
    public long Id { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>File location relative to the <c>DigitalLibrary</c> storage folder (<c>yyyy/MM/{guid}.pdf</c>). Never an absolute path; never returned to clients.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Upper-case extension of the validated file: PDF, JPG, JPEG or PNG.</summary>
    public string FileType { get; set; } = string.Empty;

    public bool IsImportant { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;

    /// <summary>Admin portal account (<c>admin_profiles.id</c>) that created the record, from the token. No foreign key (same as Form 16).</summary>
    public long CreatedBy { get; set; }

    public DateTime? UpdatedAtIst { get; set; }
    public long? UpdatedBy { get; set; }
}
