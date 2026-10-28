using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>
/// Explicit mapping for the personnel module. Ciphertext and hashes never leave the entity; the sensitive plaintext
/// fields of a request are handled by the service (encrypt + hash), not here.
/// </summary>
public static class UserPisMappingExtensions
{
    public static UserPisListItemDto ToListItem(this UserPis p) => new()
    {
        Id = p.Id,
        NsgNumber = p.NsgNumber,
        ServiceNumber = p.ServiceNumber,
        FullName = p.FullName,
        NsgRankId = p.NsgRankId,
        NsgRankName = p.NsgRank?.RankName,
        NsgTradeId = p.NsgTradeId,
        NsgTradeName = p.NsgTrade?.NsgTradeName,
        ParentDepartmentRankId = p.ParentDepartmentRankId,
        ParentDepartmentRankName = p.ParentDepartmentRank?.Name,
        PdTradeId = p.PdTradeId,
        PdTradeName = p.PdTrade?.PdTradeName,
        NsgUnitId = p.NsgUnitId,
        NsgUnitName = p.NsgUnit?.Name,
        EducationId = p.EducationId,
        EducationName = p.Education?.CourseName,
        DateOfInductionInNsg = p.DateOfInductionInNsg,
        SideSteppedOn = p.SideSteppedOn,
        SideSteppedFromNsgUnitId = p.SideSteppedFromNsgUnitId,
        SideSteppedFromNsgUnitName = p.SideSteppedFromNsgUnit?.Name,
        DateOfRepatriation = p.DateOfRepatriation,
        DateOfBirth = p.DateOfBirth,
        DateOfEnrollmentInGovtService = p.DateOfEnrollmentInGovtService,
        DateOfSuperannuation = p.DateOfSuperannuation,
        Gender = p.Gender,
        Category = p.Category,
        ReligionId = p.ReligionId,
        ReligionName = p.Religion?.Name,
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
        PermanentStateId = p.PermanentStateId,
        PermanentStateName = p.PermanentState?.Name,
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
        NsgRankId = p.NsgRankId,
        NsgRankName = p.NsgRank?.RankName,
        NsgTradeId = p.NsgTradeId,
        NsgTradeName = p.NsgTrade?.NsgTradeName,
        ParentDepartmentRankId = p.ParentDepartmentRankId,
        ParentDepartmentRankName = p.ParentDepartmentRank?.Name,
        PdTradeId = p.PdTradeId,
        PdTradeName = p.PdTrade?.PdTradeName,
        NsgUnitId = p.NsgUnitId,
        NsgUnitName = p.NsgUnit?.Name,
        EducationId = p.EducationId,
        EducationName = p.Education?.CourseName,
        DateOfInductionInNsg = p.DateOfInductionInNsg,
        SideSteppedOn = p.SideSteppedOn,
        SideSteppedFromNsgUnitId = p.SideSteppedFromNsgUnitId,
        SideSteppedFromNsgUnitName = p.SideSteppedFromNsgUnit?.Name,
        DateOfRepatriation = p.DateOfRepatriation,
        DateOfBirth = p.DateOfBirth,
        DateOfEnrollmentInGovtService = p.DateOfEnrollmentInGovtService,
        DateOfSuperannuation = p.DateOfSuperannuation,
        Gender = p.Gender,
        Category = p.Category,
        ReligionId = p.ReligionId,
        ReligionName = p.Religion?.Name,
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
        PermanentStateId = p.PermanentStateId,
        PermanentStateName = p.PermanentState?.Name,
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
    /// Copies the writable, non-sensitive fields onto the entity. Id, NSG number, activation status, creation metadata,
    /// and the encrypted/hashed columns are never touched here.
    /// </summary>
    public static void ApplyTo(this UserPisWriteDto r, UserPis p)
    {
        p.ServiceNumber = PisNormalizer.Identifier(r.ServiceNumber);
        p.NsgRankId = r.NsgRankId;
        p.ParentDepartmentRankId = r.ParentDepartmentRankId;
        p.NsgTradeId = r.NsgTradeId;
        p.PdTradeId = r.PdTradeId;
        p.FullName = Clean(r.FullName);
        p.NsgUnitId = r.NsgUnitId;
        p.EducationId = r.EducationId;
        p.DateOfInductionInNsg = r.DateOfInductionInNsg;
        p.SideSteppedOn = r.SideSteppedOn;
        p.SideSteppedFromNsgUnitId = r.SideSteppedFromNsgUnitId;
        p.DateOfRepatriation = r.DateOfRepatriation;
        p.DateOfBirth = r.DateOfBirth;
        p.DateOfEnrollmentInGovtService = r.DateOfEnrollmentInGovtService;
        p.DateOfSuperannuation = r.DateOfSuperannuation;
        if (r.Gender.HasValue) p.Gender = r.Gender; // a PUT that omits gender keeps the stored value
        p.Category = r.Category;
        p.ReligionId = r.ReligionId;
        p.FatherName = Clean(r.FatherName);
        p.MotherName = Clean(r.MotherName);
        p.SpouseName = Clean(r.SpouseName);
        p.NextOfKinMobileNumber = Clean(r.NextOfKinMobileNumber);
        p.NumberOfChildren = r.NumberOfChildren;
        p.MobileNumber = Clean(r.MobileNumber);
        p.BloodGroup = r.BloodGroup;
        p.HeightCm = r.HeightCm.HasValue ? checked((byte)r.HeightCm.Value) : null; // range-validated 1-255 beforehand
        p.WeightKg = r.WeightKg;
        p.PermanentVillage = Clean(r.PermanentVillage);
        p.PermanentPostOffice = Clean(r.PermanentPostOffice);
        p.PermanentPoliceStation = Clean(r.PermanentPoliceStation);
        p.PermanentDistrict = Clean(r.PermanentDistrict);
        p.PermanentStateId = r.PermanentStateId;
        p.PermanentPinCode = Clean(r.PermanentPinCode);
        p.DateOfAme = r.DateOfAme;
        p.DateOfImmovablePropertyReturn = r.DateOfImmovablePropertyReturn;
        p.EarnedLeaveDue = r.EarnedLeaveDue;
        p.HalfPayLeaveDue = r.HalfPayLeaveDue;
        p.PaternityLeaveAvailed = Clean(r.PaternityLeaveAvailed);
        p.MaternityLeaveAvailed = Clean(r.MaternityLeaveAvailed);
        p.ChildCareLeaveAvailed = Clean(r.ChildCareLeaveAvailed);
    }

    // Blank text is stored as NULL, never as an empty string (a unique mobile column must not hold '' twice).
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
