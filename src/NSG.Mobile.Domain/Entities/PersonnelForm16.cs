using NSG.Mobile.Domain.Common;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// The Form 16 (income tax certificate) of one person for one assessment year. Table <c>personnel_form16</c>.
/// One row per (<see cref="NsgNumber"/>, <see cref="AssessmentYear"/>); Part A and Part B are uploaded independently and
/// either may be absent. The row is created by the first upload and only ever updated afterwards.
/// </summary>
public class PersonnelForm16
{
    public long Id { get; set; }

    /// <summary>NSG number of the person, exactly as held in <c>users_pis.nsg_number</c>. Soft reference (no foreign key, ADR-020).</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Assessment year as <c>yyyy-yy</c>, for example <c>2026-27</c>.</summary>
    public string AssessmentYear { get; set; } = string.Empty;

    /// <summary>Part A PDF, relative to the Form 16 storage root; null while no Part A has been uploaded. Never returned to clients.</summary>
    public string? PartAFilePath { get; set; }

    /// <summary>Part B PDF, relative to the Form 16 storage root; null while no Part B has been uploaded. Never returned to clients.</summary>
    public string? PartBFilePath { get; set; }

    public DateTime? PartAUploadedAtIst { get; set; }
    public DateTime? PartBUploadedAtIst { get; set; }

    /// <summary>Admin portal account (<c>admin_profiles.id</c>) that made the latest upload, taken from the token. No foreign key.</summary>
    public long UploadedBy { get; set; }

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public DateTime UpdatedAtIst { get; set; } = IndianStandardTime.Now;
}
