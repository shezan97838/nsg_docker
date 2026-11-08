namespace NSG.Mobile.Application.Profile.DTOs;

/// <summary>
/// Full PIS details of the signed-in mobile user for GET /api/v1/profile. SOW §3.1 (PIS).
///
/// The personnel row is a self-contained text model: every column is returned exactly as stored (lookup values such as rank and
/// unit are plain text, and numeric-looking columns such as height, weight and leave details are text and are never converted).
///
/// SECURITY: returns the caller's own Aadhaar, PAN, PRAN and salary account number decrypted (the person is taken from
/// the token only, so nobody else's can be read). Never returns ciphertext, and deliberately excludes all admin-only fields
/// (MobileAppActivationStatus, IsActive, audit timestamps and user ids), no MPIN or password hash.
/// </summary>
public class MobileProfileResponseDto
{
    // ── Identity and service ──

    public string NsgNumber { get; set; } = string.Empty;
    public string? ServiceNumber { get; set; }
    public string? FullName { get; set; }

    public string? NsgRank { get; set; }
    public string? ParentDepartmentRank { get; set; }
    public string? NsgUnit { get; set; }
    public string? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Side-stepped details as stored (one free-text value, for example <c>10.01.2025 FROM 11 SRG</c>).</summary>
    public string? SideSteppedDetails { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    // ── Personal and family ──

    public string? Gender { get; set; }
    public string? Category { get; set; }
    public string? Religion { get; set; }

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>Spouse name.</summary>
    public string? SpouseName { get; set; }

    /// <summary>Mobile number of next of kin.</summary>
    public string? NextOfKinMobileNumber { get; set; }

    public string? NumberOfChildren { get; set; }

    /// <summary>Individual's own mobile number.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    public string? BloodGroup { get; set; }

    /// <summary>Height as stored (text).</summary>
    public string? HeightCm { get; set; }

    /// <summary>Weight as stored (text).</summary>
    public string? WeightKg { get; set; }

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

    public string? PermanentState { get; set; }

    /// <summary>PIN code as stored (text).</summary>
    public string? PermanentPinCode { get; set; }

    // ── Service and leave summaries ──

    public DateOnly? DateOfAme { get; set; }

    /// <summary>Date of Immovable Property Return (Optional in source).</summary>
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }

    /// <summary>Earned/Annual leave details still due, as stored (text).</summary>
    public string? EarnedLeaveDue { get; set; }

    /// <summary>Half Pay leave details still due, as stored (text).</summary>
    public string? HalfPayLeaveDue { get; set; }

    /// <summary>Paternity leave already availed.</summary>
    public string? PaternityLeaveAvailed { get; set; }

    /// <summary>Maternity leave already availed.</summary>
    public string? MaternityLeaveAvailed { get; set; }

    /// <summary>Child care leave already availed.</summary>
    public string? ChildCareLeaveAvailed { get; set; }
}
