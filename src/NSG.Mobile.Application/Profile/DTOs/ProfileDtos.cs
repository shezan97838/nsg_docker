using System.Text.Json.Serialization;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Profile.DTOs;

/// <summary>
/// Full PIS details of the signed-in mobile user for GET /api/v1/profile. SOW §3.1 (PIS).
///
/// SECURITY: returns the caller's own Aadhaar, PAN, PRAN and salary account number decrypted (the person is taken from
/// the token only, so nobody else's can be read). Never returns ciphertext or blind-index hashes, and deliberately excludes
/// all admin-only fields (MobileAppActivationStatus, IsActive, audit timestamps and user ids), no MPIN or password hash. Lookup ids are included alongside their display names so the mobile client can
/// display and match values without a second round-trip.
/// </summary>
public class MobileProfileResponseDto
{
    // ── Identity and service ──

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

    /// <summary>Date of side-stepping.</summary>
    public DateOnly? SideSteppedOn { get; set; }

    /// <summary>NSG unit the person was side-stepped from.</summary>
    public int? SideSteppedFromNsgUnitId { get; set; }
    public string? SideSteppedFromNsgUnitName { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    // ── Personal and family ──

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Gender? Gender { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PersonnelCategory? Category { get; set; }

    public int? ReligionId { get; set; }
    public string? ReligionName { get; set; }

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>Spouse name.</summary>
    public string? SpouseName { get; set; }

    /// <summary>Mobile number of next of kin.</summary>
    public string? NextOfKinMobileNumber { get; set; }

    public int? NumberOfChildren { get; set; }

    /// <summary>Individual's own mobile number.</summary>
    public string? MobileNumber { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BloodGroupType? BloodGroup { get; set; }

    /// <summary>Height in whole centimetres.</summary>
    public int? HeightCm { get; set; }

    /// <summary>Weight in kilograms, up to two decimals.</summary>
    public decimal? WeightKg { get; set; }

    // Sensitive identifiers: decrypted for the owner only; null when not recorded or when the stored value cannot be read.

    /// <summary>Aadhaar number (12 digits).</summary>
    public string? AadhaarNumber { get; set; }

    /// <summary>PAN (upper-case).</summary>
    public string? PanNumber { get; set; }

    /// <summary>PRAN.</summary>
    public string? PranNumber { get; set; }

    /// <summary>Salary account number.</summary>
    public string? SalaryAccountNumber { get; set; }

    // ── Permanent address ──

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }

    /// <summary>District as text (no District master).</summary>
    public string? PermanentDistrict { get; set; }

    public int? PermanentStateId { get; set; }
    public string? PermanentStateName { get; set; }

    /// <summary>Six-digit PIN code.</summary>
    public string? PermanentPinCode { get; set; }

    // ── Service and leave summaries ──

    public DateOnly? DateOfAme { get; set; }

    /// <summary>Date of Immovable Property Return (Optional in source).</summary>
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }

    /// <summary>Earned/Annual leave balance still due.</summary>
    public decimal? EarnedLeaveDue { get; set; }

    /// <summary>Half Pay leave balance still due.</summary>
    public decimal? HalfPayLeaveDue { get; set; }

    /// <summary>Paternity leave already availed.</summary>
    public string? PaternityLeaveAvailed { get; set; }

    /// <summary>Maternity leave already availed.</summary>
    public string? MaternityLeaveAvailed { get; set; }

    /// <summary>Child care leave already availed.</summary>
    public string? ChildCareLeaveAvailed { get; set; }
}
