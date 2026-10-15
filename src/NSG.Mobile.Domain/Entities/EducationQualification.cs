namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Education qualification lookup (workbook column "EDUCATION", for example "B.Tech"). Table <c>education_qualifications</c>.
/// Independent lookup: the only reference to it is the optional <see cref="UserPis.EducationId"/>.
/// </summary>
public class EducationQualification
{
    public int Id { get; set; }

    /// <summary>Course name. Required and unique within this table (case-insensitive under the default MySQL collation).</summary>
    public string CourseName { get; set; } = string.Empty;

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public bool IsActive { get; set; } = true;
}
