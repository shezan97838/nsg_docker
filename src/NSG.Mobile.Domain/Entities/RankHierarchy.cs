namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Represents an official rank within the National Security Guard (NSG) hierarchy.
/// Used by administrators for rank assignment and hierarchy management.
/// </summary>
public class RankHierarchy
{
    public int Id { get; set; }

    /// <summary>
    /// Seniority rank order number according to official NSG hierarchy.
    /// </summary>
    public int SrNo { get; set; }

    /// <summary>
    /// Official rank designation name (e.g., "DG", "Group Commander").
    /// </summary>
    public string RankName { get; set; } = string.Empty;

    /// <summary>
    /// Short code or abbreviation for the rank (e.g., "DG", "GC").
    /// </summary>
    public string? ShortCode { get; set; }

    /// <summary>
    /// Indicates whether this rank is currently active for assignment.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime UpdatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
