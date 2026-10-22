namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Message from the Director General shown to employees. Table <c>dg_message</c>. Soft delete only: <see cref="IsDeleted"/> hides the row,
/// <see cref="IsActive"/> hides it from the mobile app without deleting it. Timestamps are IST instants; <see cref="CreatedBy"/> and
/// <see cref="UpdatedBy"/> are <c>admin_profiles.id</c> values taken from the token (no foreign key, like the other admin id columns).
/// </summary>
public class DgMessage
{
    public int Id { get; set; }
    public string DgName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;

    /// <summary>The message text; paragraphs are separated by line breaks and kept as typed.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Object key of the image in the private bucket (never a URL); null when there is no image.</summary>
    public string? ImageStorageKey { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public long? UpdatedBy { get; set; }
}
