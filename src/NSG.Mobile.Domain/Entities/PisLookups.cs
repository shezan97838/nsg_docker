namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Base shape of the simple PIS lookup masters (name list maintained by administrators).
/// No hierarchy is modelled: a parent/child unit structure has not been confirmed by an approved source.
/// </summary>
public abstract class PisLookup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime? UpdatedAtIst { get; set; }
}

/// <summary>NSG unit (workbook column "NSG UNIT", e.g. "13 SRG", "HQ NSG").</summary>
public class NsgUnit : PisLookup { }

/// <summary>Religion (workbook column "RELIGION").</summary>
public class Religion : PisLookup { }

/// <summary>Rank in the parent (cadre) department (workbook column "RANK PARENT DEPARTMENT").</summary>
public class ParentDepartmentRank : PisLookup { }
