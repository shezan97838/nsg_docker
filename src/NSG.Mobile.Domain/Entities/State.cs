using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// An Indian State or Union Territory (first-level administrative division).
/// </summary>
public class State
{
    public int Id { get; set; }

    /// <summary>Official administrative name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Official Local Government Directory (LGD) state code, when one exists.</summary>
    public string? Code { get; set; }

    public AdministrativeUnitType AdministrativeType { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime? UpdatedAtIst { get; set; }

    public ICollection<City> Cities { get; set; } = new List<City>();
}
