using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// NSG Personnel Information System (PIS) master record — one row per person in the PIS workbook.
/// Table: <c>users_pis</c>. Independent of <see cref="User"/> (the mobile-app account); linking the two is a later phase.
///
/// Identity rules: <see cref="NsgNumber"/> is mandatory and globally unique and is immutable after insert;
/// <see cref="ServiceNumber"/> is mandatory but NOT unique (people from different forces can share one).
/// Both are strings (leading zeros/letters preserved) and are never used as foreign keys.
///
/// Only the identifiers are mandatory: the workbook carries no Mandatory/Optional markers other than "(Optional)" on
/// the property-return date, so no other field is forced non-null until the approved M/O list is supplied.
/// </summary>
public class UserPis
{
    /// <summary>Surrogate key (MySQL BIGINT). Never exposed as a business identifier; use <see cref="NsgNumber"/>.</summary>
    public long Id { get; set; }

    // ── Identity and service ──

    /// <summary>NSG number (workbook "NSG NO"). Required, unique, stored exactly as supplied.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>IRLA / Force / Army number (workbook "IRLA/ FORCE NO./ARMY NO."). Required, not unique.</summary>
    public string ServiceNumber { get; set; } = string.Empty;

    public int? NsgRankId { get; set; }
    public RankHierarchy? NsgRank { get; set; }

    public int? ParentDepartmentRankId { get; set; }
    public ParentDepartmentRank? ParentDepartmentRank { get; set; }

    /// <summary>NSG trade (for example GD). Optional; stored in column nsg_trade_id. Independent of the rank.</summary>
    public int? NsgTradeId { get; set; }
    public NsgTrade? NsgTrade { get; set; }

    /// <summary>Parent-department trade. Optional; stored in column pd_trade_id. Independent of the rank and of <see cref="NsgTradeId"/>.</summary>
    public int? PdTradeId { get; set; }
    public PdTrade? PdTrade { get; set; }

    public string? FullName { get; set; }

    public int? NsgUnitId { get; set; }
    public NsgUnit? NsgUnit { get; set; }

    public int? EducationId { get; set; }
    public EducationQualification? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>Date of side-stepping (first half of workbook "DATE OF SIDE STEPPED &amp; NSG UNIT").</summary>
    public DateOnly? SideSteppedOn { get; set; }

    /// <summary>NSG unit the person was side-stepped from (second half of the same workbook column).</summary>
    public int? SideSteppedFromNsgUnitId { get; set; }
    public NsgUnit? SideSteppedFromNsgUnit { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    // ── Personal and family ──

    public PersonnelCategory? Category { get; set; }

    public int? ReligionId { get; set; }
    public Religion? Religion { get; set; }

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>Spouse name (the workbook column is titled "NAME OF WIFE"; the approved field name is SpouseName).</summary>
    public string? SpouseName { get; set; }

    /// <summary>Mobile number of next of kin. Stored as text, not unique.</summary>
    public string? NextOfKinMobileNumber { get; set; }

    public int? NumberOfChildren { get; set; }

    /// <summary>Individual's own mobile number. Unique across all retained PIS records; stored exactly as supplied.</summary>
    public string? MobileNumber { get; set; }

    public BloodGroupType? BloodGroup { get; set; }

    /// <summary>Height in whole centimetres (e.g. 172). MySQL TINYINT UNSIGNED, so 0-255; fractional heights are not stored.</summary>
    public byte? HeightCm { get; set; }
    /// <summary>Weight in kilograms with up to two decimals (e.g. 81.96). MySQL DECIMAL(5,2).</summary>
    public decimal? WeightKg { get; set; }

    // ── Sensitive identifiers (ciphertext only) ──
    // Values are produced by IEncryptionService (AES-256, key from configuration) in the Application layer before
    // they reach this entity. The database never holds the plaintext, and these must never appear in list DTOs or logs.

    public string? AadhaarNumberEncrypted { get; set; }
    public string? PanNumberEncrypted { get; set; }
    public string? PranNumberEncrypted { get; set; }
    public string? SalaryAccountNumberEncrypted { get; set; }

    // Deterministic keyed hashes (HMAC-SHA256, lower-case hex, 64 chars) of the normalised plaintext. They exist only
    // because the *Encrypted columns use a random IV and so cannot carry a unique index. NULL when the identifier is NULL.

    /// <summary>Blind index of the upper-case PAN. Unique (approved). Computed by the Application layer before save.</summary>
    public string? PanNumberHash { get; set; }

    /// <summary>Blind index of the digits-only salary account number. Unique (approved). Computed by the Application layer before save.</summary>
    public string? SalaryAccountNumberHash { get; set; }

    // ── Permanent address ──

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }

    /// <summary>District as text: no District master exists and none is approved yet.</summary>
    public string? PermanentDistrict { get; set; }

    public int? PermanentStateId { get; set; }
    public State? PermanentState { get; set; }

    /// <summary>Six-digit PIN code, kept as text.</summary>
    public string? PermanentPinCode { get; set; }

    // ── Service and leave summaries ──

    public DateOnly? DateOfAme { get; set; }

    /// <summary>Workbook "DATE OF IMMOVABLE PROPERTY RETURN (IPR)" — marked "(Optional)" in the source.</summary>
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }

    /// <summary>"DETAILS OF EARNED/ANNUAL LEAVE DUE" — quantity still due. Unit not stated in the source.</summary>
    public decimal? EarnedLeaveDue { get; set; }

    /// <summary>"DETAILS OF HALF PAY LEAVE DUE" — quantity still due. Unit not stated in the source.</summary>
    public decimal? HalfPayLeaveDue { get; set; }

    // The three "AVAILED" columns record leave already taken, NOT a balance. The source mixes counts and text
    // (e.g. "2 TIMES", "0"), so the text is preserved verbatim until the unit is confirmed.
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }

    // ── Mobile app activation ──

    /// <summary>
    /// Approval state of this person's request to activate the mobile app. New records start as Pending.
    /// This is an onboarding decision only; it is not a login or session status.
    /// </summary>
    public MobileAppActivationStatus MobileAppActivationStatus { get; set; } = MobileAppActivationStatus.Pending;

    // ── Metadata ──

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    /// <summary>User id of the creator (no FK, following <see cref="User.ApprovedBy"/>).</summary>
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public int? UpdatedBy { get; set; }
}
