namespace NSG.Mobile.Application.Profile.DTOs;

/// <summary>
/// Basic PIS details of the signed-in mobile user for GET /api/v1/profile. SOW §3.1 (PIS).
///
/// IMPORTANT: deliberately minimal. Contains no Aadhaar/PAN/PRAN/salary-account data (encrypted or hashed), no MPIN or
/// password hash, no internal ids and no family or address details.
/// </summary>
public class MobileProfileResponseDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public string ServiceNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? RankName { get; set; }
    public string? ParentDepartmentRankName { get; set; }
    public string? NsgTradeName { get; set; }
    public string? NsgUnitName { get; set; }
    public string? Category { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfInductionInNsg { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }
    public string? MobileNumber { get; set; }
}
