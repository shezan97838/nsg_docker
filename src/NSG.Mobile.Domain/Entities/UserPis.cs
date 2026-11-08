using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// NSG Personnel Information System (PIS) master record — one row per person in the PIS workbook (Sheet1).
/// Table: <c>users_pis</c>. Independent of <see cref="User"/> (the mobile-app account).
///
/// Self-contained model: there are no foreign keys, lookup ids or navigation properties. Every workbook column is a direct
/// value on this row: text columns are strings (even when the value looks numeric), and exactly seven columns are real
/// dates (<see cref="DateOnly"/>). The four sensitive identifiers are stored as reversible ciphertext only.
///
/// Mandatory and unique: <see cref="NsgNumber"/> (immutable after insert) and <see cref="MobileNumber"/>.
/// <see cref="ServiceNumber"/> and every other personnel field are nullable.
/// </summary>
public class UserPis
{
    /// <summary>Surrogate key (MySQL BIGINT). Never exposed as a business identifier; use <see cref="NsgNumber"/>.</summary>
    public long Id { get; set; }

    // ── Identity and service (workbook text columns) ──

    /// <summary>Workbook "NSG NO". Required, globally unique, stored exactly as supplied.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Workbook "IRLA/FORCE NO./ARMY NO.". Nullable, not unique.</summary>
    public string? ServiceNumber { get; set; }

    /// <summary>Workbook "RANK (NSG)".</summary>
    public string? NsgRank { get; set; }

    /// <summary>Workbook "RANK PARENT DEPARTMENT".</summary>
    public string? ParentDepartmentRank { get; set; }

    /// <summary>Workbook "NAME".</summary>
    public string? FullName { get; set; }

    /// <summary>Workbook "NSG UNIT".</summary>
    public string? NsgUnit { get; set; }

    /// <summary>Workbook "EDUCATION".</summary>
    public string? Education { get; set; }

    public DateOnly? DateOfInductionInNsg { get; set; }

    /// <summary>
    /// Workbook "DATE OF SIDE STEPPED &amp; NSG UNIT", always one free-text value such as <c>10.01.2025 FROM 11 SRG</c>.
    /// Never split or parsed.
    /// </summary>
    public string? SideSteppedDetails { get; set; }

    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }

    // ── Personal and family (all text) ──

    /// <summary>Workbook "Gender" as free text.</summary>
    public string? Gender { get; set; }

    /// <summary>Workbook "CATEGORY (SC/ST/GEN/OBC)" as free text.</summary>
    public string? Category { get; set; }

    /// <summary>Workbook "RELIGION".</summary>
    public string? Religion { get; set; }

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>Workbook "NAME OF SPOUSE".</summary>
    public string? SpouseName { get; set; }

    /// <summary>Workbook "MOBILE NO. OF NOK". Text, not unique.</summary>
    public string? NextOfKinMobileNumber { get; set; }

    /// <summary>Workbook "NO OF CHILDREN", kept as text.</summary>
    public string? NumberOfChildren { get; set; }

    /// <summary>Workbook "MOBILE NO OF INDIVIDUAL". Required and unique across all retained PIS records; ten digits.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Workbook "BLOOD GROUP" as free text.</summary>
    public string? BloodGroup { get; set; }

    /// <summary>Workbook "HEIGHT (IN CM)", kept as text.</summary>
    public string? HeightCm { get; set; }

    /// <summary>Workbook "WEIGHT IN KGs", kept as text.</summary>
    public string? WeightKg { get; set; }

    // ── Sensitive identifiers (ciphertext only) ──
    // Values are produced by IEncryptionService (AES-256, key from configuration) in the Application layer before
    // they reach this entity. The database never holds the plaintext. There are no hash columns, no blind indexes and no
    // unique constraints on these identifiers.

    public string? AadhaarNumberEncrypted { get; set; }
    public string? PanNumberEncrypted { get; set; }
    public string? PranNumberEncrypted { get; set; }
    public string? SalaryAccountNumberEncrypted { get; set; }

    // ── Permanent address ──

    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }

    /// <summary>Workbook "ADDRESS - STATE" as text.</summary>
    public string? PermanentState { get; set; }

    /// <summary>Workbook "ADDRESS - PIN", kept as text.</summary>
    public string? PermanentPinCode { get; set; }

    // ── Service and leave summaries ──

    public DateOnly? DateOfAme { get; set; }

    /// <summary>Workbook "DATE OF IMMOVABLE PROPERTY RETURN (IPR)".</summary>
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }

    // The leave columns are text: the source mixes counts and descriptions (e.g. "2 TIMES", "0").
    public string? EarnedLeaveDue { get; set; }
    public string? HalfPayLeaveDue { get; set; }
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

    /// <summary>
    /// <c>admin_profiles.id</c> of the PIS Admin currently responsible for this record. Null = unassigned (every existing record).
    /// Not <see cref="CreatedBy"/>: that column keeps its own meaning. No FK, like the rest of this table.
    /// </summary>
    public long? AssignedTo { get; set; }
}
