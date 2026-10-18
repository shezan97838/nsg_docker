using System.Text.Json.Serialization;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.DTOs;

/// <summary>
/// Fields an administrator may set on a personnel (PIS) record. Server-managed values — Id, the activation status,
/// the encrypted/hashed columns and the audit metadata — are deliberately absent, so a client cannot supply them.
/// The four sensitive identifiers arrive here in plaintext, are validated, then encrypted; they are never returned.
/// </summary>
public abstract class UserPisWriteDto
{
    /// <summary>IRLA / force / army number. Required; not unique.</summary>
    public string ServiceNumber { get; set; } = string.Empty;

    public int? NsgRankId { get; set; }
    public int? ParentDepartmentRankId { get; set; }

    /// <summary>Optional NSG trade (for example GD). Must exist when supplied; independent of the rank.</summary>
    public int? NsgTradeId { get; set; }

    /// <summary>Optional parent-department trade. Must exist when supplied; independent of the rank and the NSG trade.</summary>
    public int? PdTradeId { get; set; }

    public string? FullName { get; set; }
    public int? NsgUnitId { get; set; }
    public int? EducationId { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Date of side-stepping; stored separately from <see cref="SideSteppedFromNsgUnitId"/>.</summary>
    public DateOnly? SideSteppedOn { get; set; }

    /// <summary>NSG unit the person was side-stepped from; stored separately from <see cref="SideSteppedOn"/>.</summary>
    public int? SideSteppedFromNsgUnitId { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PersonnelCategory? Category { get; set; }

    public int? ReligionId { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public int? NumberOfChildren { get; set; }

    /// <summary>Individual's own mobile number: exactly 10 digits. Unique.</summary>
    public string? MobileNumber { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BloodGroupType? BloodGroup { get; set; }

    /// <summary>Height in whole centimetres (1-255). A fractional value is rejected at deserialisation.</summary>
    public int? HeightCm { get; set; }

    /// <summary>Weight in kilograms, up to two decimals.</summary>
    public decimal? WeightKg { get; set; }

    // Sensitive identifiers (plaintext in, never out). On update, null means "leave unchanged".
    public string? AadhaarNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PranNumber { get; set; }
    public string? SalaryAccountNumber { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public int? PermanentStateId { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public decimal? EarnedLeaveDue { get; set; }
    public decimal? HalfPayLeaveDue { get; set; }
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }
}

/// <summary>Request to create a personnel record. New records are active and start as activation-Pending.</summary>
public class CreateUserPisRequestDto : UserPisWriteDto
{
    /// <summary>NSG number. Required, unique, trimmed, and immutable after creation.</summary>
    public string NsgNumber { get; set; } = string.Empty;
}

/// <summary>
/// Request to update a personnel record. The NSG number, activation status and audit metadata cannot be changed here.
/// All non-sensitive fields are replaced (null clears); the four sensitive identifiers are replaced only when supplied.
/// </summary>
public class UpdateUserPisRequestDto : UserPisWriteDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>Search and filter options for the personnel list.</summary>
public class UserPisFilter
{
    /// <summary>Matches NSG number, service number, name or mobile number (contains). Sensitive fields are never searched.</summary>
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int? NsgRankId { get; set; }
    public int? NsgUnitId { get; set; }
    public int? ParentDepartmentRankId { get; set; }
    public PersonnelCategory? Category { get; set; }
    public MobileAppActivationStatus? MobileAppActivationStatus { get; set; }
}

/// <summary>
/// Row for the personnel list: every personnel field of <see cref="UserPisResponseDto"/> except the audit metadata
/// (created/updated timestamps and user ids) and the Aadhaar/PAN/PRAN/salary-account identifiers, which are not
/// returned in bulk (not even masked) and remain available only from the single-record endpoint.
/// </summary>
public class UserPisListItemDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string ServiceNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }

    public int? NsgRankId { get; set; }
    public string? NsgRankName { get; set; }
    public int? NsgTradeId { get; set; }
    public string? NsgTradeName { get; set; }
    public int? ParentDepartmentRankId { get; set; }
    public string? ParentDepartmentRankName { get; set; }
    public int? PdTradeId { get; set; }
    public string? PdTradeName { get; set; }
    public int? NsgUnitId { get; set; }
    public string? NsgUnitName { get; set; }
    public int? EducationId { get; set; }
    public string? EducationName { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }
    public DateOnly? SideSteppedOn { get; set; }
    public int? SideSteppedFromNsgUnitId { get; set; }
    public string? SideSteppedFromNsgUnitName { get; set; }
    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PersonnelCategory? Category { get; set; }

    public int? ReligionId { get; set; }
    public string? ReligionName { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public int? NumberOfChildren { get; set; }
    public string? MobileNumber { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BloodGroupType? BloodGroup { get; set; }

    public int? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public int? PermanentStateId { get; set; }
    public string? PermanentStateName { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public decimal? EarnedLeaveDue { get; set; }
    public decimal? HalfPayLeaveDue { get; set; }
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileAppActivationStatus MobileAppActivationStatus { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Full personnel record. Aadhaar, PAN, PRAN and salary account number appear only masked (last four characters);
/// the ciphertext and the uniqueness hashes are never returned.
/// </summary>
public class UserPisResponseDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string ServiceNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }

    public int? NsgRankId { get; set; }
    public string? NsgRankName { get; set; }
    public int? NsgTradeId { get; set; }
    public string? NsgTradeName { get; set; }
    public int? ParentDepartmentRankId { get; set; }
    public string? ParentDepartmentRankName { get; set; }
    public int? PdTradeId { get; set; }
    public string? PdTradeName { get; set; }
    public int? NsgUnitId { get; set; }
    public string? NsgUnitName { get; set; }
    public int? EducationId { get; set; }
    public string? EducationName { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }
    public DateOnly? SideSteppedOn { get; set; }
    public int? SideSteppedFromNsgUnitId { get; set; }
    public string? SideSteppedFromNsgUnitName { get; set; }
    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PersonnelCategory? Category { get; set; }

    public int? ReligionId { get; set; }
    public string? ReligionName { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public int? NumberOfChildren { get; set; }
    public string? MobileNumber { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BloodGroupType? BloodGroup { get; set; }

    public int? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }

    /// <summary>Masked, e.g. XXXXXXXX9532. Null when not recorded.</summary>
    public string? AadhaarNumberMasked { get; set; }
    public string? PanNumberMasked { get; set; }
    public string? PranNumberMasked { get; set; }
    public string? SalaryAccountNumberMasked { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public int? PermanentStateId { get; set; }
    public string? PermanentStateName { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public decimal? EarnedLeaveDue { get; set; }
    public decimal? HalfPayLeaveDue { get; set; }
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileAppActivationStatus MobileAppActivationStatus { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public int? UpdatedBy { get; set; }
}
