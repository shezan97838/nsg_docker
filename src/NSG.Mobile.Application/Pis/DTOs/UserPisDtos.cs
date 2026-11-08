using System.Text.Json.Serialization;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.DTOs;

/// <summary>
/// Fields an administrator may set on a personnel (PIS) record. Server-managed values — Id, the activation status,
/// the encrypted columns and the audit metadata — are deliberately absent, so a client cannot supply them.
/// The four sensitive identifiers arrive here in plaintext, are validated, then encrypted; they are never returned.
///
/// The personnel row is a self-contained text model: lookup values (rank, unit, trade, education, religion, state) are plain text,
/// not ids, and are stored exactly as sent (trimmed; blank becomes null). Numeric-looking columns (height, weight, children, leave
/// details) are text too: a JSON number is accepted and stored as its digits, never converted back to a number.
/// </summary>
public abstract class UserPisWriteDto
{
    /// <summary>IRLA / force / army number. Required by this API; not unique.</summary>
    public string ServiceNumber { get; set; } = string.Empty;

    public string? NsgRank { get; set; }
    public string? ParentDepartmentRank { get; set; }

    public string? FullName { get; set; }
    public string? NsgUnit { get; set; }
    public string? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Side-stepped details as one free-text value, for example <c>10.01.2025 FROM 11 SRG</c>. Never split or parsed.</summary>
    public string? SideSteppedDetails { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    /// <summary>Free text (up to 30 characters), stored as sent. Required on create; on update an omitted value keeps the stored one.</summary>
    public string? Gender { get; set; }

    /// <summary>Free text (up to 30 characters), stored as sent.</summary>
    public string? Category { get; set; }

    public string? Religion { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }

    [JsonConverter(typeof(PisTextJsonConverter))]
    public string? NumberOfChildren { get; set; }

    /// <summary>Individual's own mobile number: exactly 10 digits. Required on create, unique. On update an omitted value keeps the stored one.</summary>
    public string? MobileNumber { get; set; }

    /// <summary>Free text (up to 20 characters), stored as sent.</summary>
    public string? BloodGroup { get; set; }

    [JsonConverter(typeof(PisTextJsonConverter))]
    public string? HeightCm { get; set; }

    [JsonConverter(typeof(PisTextJsonConverter))]
    public string? WeightKg { get; set; }

    // Sensitive identifiers (plaintext in, never out). On update, null means "leave unchanged".
    public string? AadhaarNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PranNumber { get; set; }
    public string? SalaryAccountNumber { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public string? PermanentState { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }

    [JsonConverter(typeof(PisTextJsonConverter))]
    public string? EarnedLeaveDue { get; set; }

    [JsonConverter(typeof(PisTextJsonConverter))]
    public string? HalfPayLeaveDue { get; set; }

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
/// All non-sensitive fields are replaced (null clears) except the mobile number and gender, which keep their stored value when
/// omitted; the four sensitive identifiers are replaced only when supplied.
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

    /// <summary>Exact rank text (case-insensitive under the default MySQL collation).</summary>
    public string? NsgRank { get; set; }
    public string? NsgUnit { get; set; }
    public string? ParentDepartmentRank { get; set; }
    public string? Category { get; set; }
    public MobileAppActivationStatus? MobileAppActivationStatus { get; set; }
}

/// <summary>
/// Row for the personnel list: every personnel field of <see cref="UserPisResponseDto"/> except the audit metadata
/// (created/updated timestamps and user ids) and the Aadhaar/PAN/PRAN/salary-account identifiers, which are not
/// returned in bulk (not even masked) and remain available only from the single-record endpoint.
/// All text columns are returned exactly as stored.
/// </summary>
public class UserPisListItemDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string? ServiceNumber { get; set; }
    public string? FullName { get; set; }

    public string? NsgRank { get; set; }
    public string? ParentDepartmentRank { get; set; }
    public string? NsgUnit { get; set; }
    public string? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Side-stepped details as stored (one free-text value).</summary>
    public string? SideSteppedDetails { get; set; }
    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    public string? Gender { get; set; }
    public string? Category { get; set; }
    public string? Religion { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public string? NumberOfChildren { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string? BloodGroup { get; set; }
    public string? HeightCm { get; set; }
    public string? WeightKg { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public string? PermanentState { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public string? EarnedLeaveDue { get; set; }
    public string? HalfPayLeaveDue { get; set; }
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileAppActivationStatus MobileAppActivationStatus { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Full personnel record. Aadhaar, PAN, PRAN and salary account number appear only masked (last four characters);
/// the ciphertext is never returned. All text columns are returned exactly as stored.
/// </summary>
public class UserPisResponseDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string? ServiceNumber { get; set; }
    public string? FullName { get; set; }

    public string? NsgRank { get; set; }
    public string? ParentDepartmentRank { get; set; }
    public string? NsgUnit { get; set; }
    public string? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Side-stepped details as stored (one free-text value).</summary>
    public string? SideSteppedDetails { get; set; }
    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    public string? Gender { get; set; }
    public string? Category { get; set; }
    public string? Religion { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public string? NumberOfChildren { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string? BloodGroup { get; set; }
    public string? HeightCm { get; set; }
    public string? WeightKg { get; set; }

    /// <summary>Masked, e.g. XXXXXXXX9532. Null when not recorded.</summary>
    public string? AadhaarNumberMasked { get; set; }
    public string? PanNumberMasked { get; set; }
    public string? PranNumberMasked { get; set; }
    public string? SalaryAccountNumberMasked { get; set; }

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public string? PermanentState { get; set; }
    public string? PermanentPinCode { get; set; }

    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public string? EarnedLeaveDue { get; set; }
    public string? HalfPayLeaveDue { get; set; }
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
