using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// A city or town belonging to a State/Union Territory. Not a district.
/// </summary>
public class City
{
    public int Id { get; set; }

    /// <summary>Official city/town name as published by the source. Not globally unique.</summary>
    public string Name { get; set; } = string.Empty;

    public int StateId { get; set; }
    public State State { get; set; } = null!;

    /// <summary>Official source identifier (e.g. census town code), only when the source provides one.</summary>
    public string? Code { get; set; }

    /// <summary>Verified urban classification, where the source supports it.</summary>
    public TownClassification? Classification { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Import provenance: dataset the record came from. Null for manually created records. Not editable via CRUD.</summary>
    public string? SourceSystem { get; set; }

    /// <summary>Import provenance: stable key of the record within <see cref="SourceSystem"/>. Not editable via CRUD.</summary>
    public string? SourceKey { get; set; }

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime? UpdatedAtIst { get; set; }
}
