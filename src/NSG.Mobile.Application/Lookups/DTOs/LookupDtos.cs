namespace NSG.Mobile.Application.Lookups.DTOs;

/// <summary>Full PIS lookup record (NSG unit, parent department rank) returned to the admin portal.</summary>
public class LookupResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
}

/// <summary>Minimal PIS lookup item for dropdown population (active records only).</summary>
public class LookupDropdownDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Request to create a PIS lookup record.</summary>
public class CreateLookupRequestDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Request to update a PIS lookup record (including activate/deactivate via IsActive).</summary>
public class UpdateLookupRequestDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
