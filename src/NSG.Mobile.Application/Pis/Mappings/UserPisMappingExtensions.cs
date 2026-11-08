using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>
/// Explicit mapping for the personnel module. The personnel row is a self-contained text model, so reads return every stored
/// text column exactly as it is (no conversion, nothing hidden) and writes store trimmed text. Ciphertext never leaves the
/// entity; the sensitive plaintext fields of a request are handled by the service (encrypt), not here.
/// </summary>
public static class UserPisMappingExtensions
{
    public static UserPisListItemDto ToListItem(this UserPis p) => new()
    {
        Id = p.Id,
        NsgNumber = p.NsgNumber,
        ServiceNumber = p.ServiceNumber,
        FullName = p.FullName,
        NsgRank = p.NsgRank,
        ParentDepartmentRank = p.ParentDepartmentRank,
        NsgUnit = p.NsgUnit,
        Education = p.Education,
        DateOfInductionInNsg = p.DateOfInductionInNsg,
        SideSteppedDetails = p.SideSteppedDetails,
        DateOfRepatriation = p.DateOfRepatriation,
        DateOfBirth = p.DateOfBirth,
        DateOfEnrollmentInGovtService = p.DateOfEnrollmentInGovtService,
        DateOfSuperannuation = p.DateOfSuperannuation,
        Gender = p.Gender,
        Category = p.Category,
        Religion = p.Religion,
        FatherName = p.FatherName,
        MotherName = p.MotherName,
        SpouseName = p.SpouseName,
        NextOfKinMobileNumber = p.NextOfKinMobileNumber,
        NumberOfChildren = p.NumberOfChildren,
        MobileNumber = p.MobileNumber,
        BloodGroup = p.BloodGroup,
        HeightCm = p.HeightCm,
        WeightKg = p.WeightKg,
        PermanentVillage = p.PermanentVillage,
        PermanentPostOffice = p.PermanentPostOffice,
        PermanentPoliceStation = p.PermanentPoliceStation,
        PermanentDistrict = p.PermanentDistrict,
        PermanentState = p.PermanentState,
        PermanentPinCode = p.PermanentPinCode,
        DateOfAme = p.DateOfAme,
        DateOfImmovablePropertyReturn = p.DateOfImmovablePropertyReturn,
        EarnedLeaveDue = p.EarnedLeaveDue,
        HalfPayLeaveDue = p.HalfPayLeaveDue,
        PaternityLeaveAvailed = p.PaternityLeaveAvailed,
        MaternityLeaveAvailed = p.MaternityLeaveAvailed,
        ChildCareLeaveAvailed = p.ChildCareLeaveAvailed,
        MobileAppActivationStatus = p.MobileAppActivationStatus,
        IsActive = p.IsActive
    };

    /// <summary>Maps the record; <paramref name="mask"/> turns a stored ciphertext into its masked display form.</summary>
    public static UserPisResponseDto ToResponse(this UserPis p, Func<string?, string?> mask) => new()
    {
        Id = p.Id,
        NsgNumber = p.NsgNumber,
        ServiceNumber = p.ServiceNumber,
        FullName = p.FullName,
        NsgRank = p.NsgRank,
        ParentDepartmentRank = p.ParentDepartmentRank,
        NsgUnit = p.NsgUnit,
        Education = p.Education,
        DateOfInductionInNsg = p.DateOfInductionInNsg,
        SideSteppedDetails = p.SideSteppedDetails,
        DateOfRepatriation = p.DateOfRepatriation,
        DateOfBirth = p.DateOfBirth,
        DateOfEnrollmentInGovtService = p.DateOfEnrollmentInGovtService,
        DateOfSuperannuation = p.DateOfSuperannuation,
        Gender = p.Gender,
        Category = p.Category,
        Religion = p.Religion,
        FatherName = p.FatherName,
        MotherName = p.MotherName,
        SpouseName = p.SpouseName,
        NextOfKinMobileNumber = p.NextOfKinMobileNumber,
        NumberOfChildren = p.NumberOfChildren,
        MobileNumber = p.MobileNumber,
        BloodGroup = p.BloodGroup,
        HeightCm = p.HeightCm,
        WeightKg = p.WeightKg,
        AadhaarNumberMasked = mask(p.AadhaarNumberEncrypted),
        PanNumberMasked = mask(p.PanNumberEncrypted),
        PranNumberMasked = mask(p.PranNumberEncrypted),
        SalaryAccountNumberMasked = mask(p.SalaryAccountNumberEncrypted),
        PermanentVillage = p.PermanentVillage,
        PermanentPostOffice = p.PermanentPostOffice,
        PermanentPoliceStation = p.PermanentPoliceStation,
        PermanentDistrict = p.PermanentDistrict,
        PermanentState = p.PermanentState,
        PermanentPinCode = p.PermanentPinCode,
        DateOfAme = p.DateOfAme,
        DateOfImmovablePropertyReturn = p.DateOfImmovablePropertyReturn,
        EarnedLeaveDue = p.EarnedLeaveDue,
        HalfPayLeaveDue = p.HalfPayLeaveDue,
        PaternityLeaveAvailed = p.PaternityLeaveAvailed,
        MaternityLeaveAvailed = p.MaternityLeaveAvailed,
        ChildCareLeaveAvailed = p.ChildCareLeaveAvailed,
        MobileAppActivationStatus = p.MobileAppActivationStatus,
        IsActive = p.IsActive,
        CreatedAtIst = p.CreatedAtIst,
        CreatedBy = p.CreatedBy,
        UpdatedAtIst = p.UpdatedAtIst,
        UpdatedBy = p.UpdatedBy
    };

    /// <summary>
    /// Copies the writable, non-sensitive fields onto the entity as trimmed text (blank becomes null). Id, NSG number, activation
    /// status, creation metadata and the encrypted columns are never touched here. The mobile number and gender are only replaced
    /// when supplied, so a PUT that omits them keeps the stored values. Every text value is stored exactly as sent (trimmed); there is
    /// no predefined list for gender, category or blood group.
    /// </summary>
    public static void ApplyTo(this UserPisWriteDto r, UserPis p)
    {
        p.ServiceNumber = PisNormalizer.Identifier(r.ServiceNumber);   // this API still requires it; the column is nullable
        p.NsgRank = Clean(r.NsgRank);
        p.ParentDepartmentRank = Clean(r.ParentDepartmentRank);
        p.FullName = Clean(r.FullName);
        p.NsgUnit = Clean(r.NsgUnit);
        p.Education = Clean(r.Education);
        p.DateOfInductionInNsg = r.DateOfInductionInNsg;
        p.SideSteppedDetails = Clean(r.SideSteppedDetails);
        p.DateOfRepatriation = r.DateOfRepatriation;
        p.DateOfBirth = r.DateOfBirth;
        p.DateOfEnrollmentInGovtService = r.DateOfEnrollmentInGovtService;
        p.DateOfSuperannuation = r.DateOfSuperannuation;
        if (!string.IsNullOrWhiteSpace(r.Gender)) p.Gender = r.Gender.Trim();
        p.Category = Clean(r.Category);
        p.Religion = Clean(r.Religion);
        p.FatherName = Clean(r.FatherName);
        p.MotherName = Clean(r.MotherName);
        p.SpouseName = Clean(r.SpouseName);
        p.NextOfKinMobileNumber = Clean(r.NextOfKinMobileNumber);
        p.NumberOfChildren = Clean(r.NumberOfChildren);
        if (!string.IsNullOrWhiteSpace(r.MobileNumber)) p.MobileNumber = r.MobileNumber.Trim();
        p.BloodGroup = Clean(r.BloodGroup);
        p.HeightCm = Clean(r.HeightCm);
        p.WeightKg = Clean(r.WeightKg);
        p.PermanentVillage = Clean(r.PermanentVillage);
        p.PermanentPostOffice = Clean(r.PermanentPostOffice);
        p.PermanentPoliceStation = Clean(r.PermanentPoliceStation);
        p.PermanentDistrict = Clean(r.PermanentDistrict);
        p.PermanentState = Clean(r.PermanentState);
        p.PermanentPinCode = Clean(r.PermanentPinCode);
        p.DateOfAme = r.DateOfAme;
        p.DateOfImmovablePropertyReturn = r.DateOfImmovablePropertyReturn;
        p.EarnedLeaveDue = Clean(r.EarnedLeaveDue);
        p.HalfPayLeaveDue = Clean(r.HalfPayLeaveDue);
        p.PaternityLeaveAvailed = Clean(r.PaternityLeaveAvailed);
        p.MaternityLeaveAvailed = Clean(r.MaternityLeaveAvailed);
        p.ChildCareLeaveAvailed = Clean(r.ChildCareLeaveAvailed);
    }

    // Blank text is stored as NULL, never as an empty string (a unique mobile column must not hold '' twice).
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
