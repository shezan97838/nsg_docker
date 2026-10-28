using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Profile.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Profile.Services;

public class MobileProfileService : IMobileProfileService
{
    public const string PisRecordNotFoundMessage = "Your personnel record was not found.";

    private readonly ICurrentUserService _currentUser;
    private readonly IMobileUserProfileRepository _profiles;
    private readonly IUserPisRepository _pis;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<MobileProfileService> _logger;

    public MobileProfileService(
        ICurrentUserService currentUser,
        IMobileUserProfileRepository profiles,
        IUserPisRepository pis,
        IEncryptionService encryption,
        ILogger<MobileProfileService> logger)
    {
        _currentUser = currentUser;
        _profiles = profiles;
        _pis = pis;
        _encryption = encryption;
        _logger = logger;
    }

    /// <summary>
    /// Decrypts a stored identifier for display only (the stored ciphertext is never modified). An unreadable value (for
    /// example a rotated key) yields null — ciphertext is never returned — and is logged without any value.
    /// </summary>
    private string? Reveal(string? cipherText, string field)
    {
        if (cipherText == null)
            return null;

        try
        {
            return _encryption.Decrypt(cipherText);
        }
        // Malformed base64 (FormatException), a truncated value (ArgumentException/OverflowException from the IV split) or a
        // wrong key / bad padding (CryptographicException) all mean "not readable".
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or OverflowException)
        {
            _logger.LogWarning("A stored sensitive personnel value ({Field}) could not be decrypted for the profile response.", field);
            return null;
        }
    }

    public async Task<Result<MobileProfileResponseDto>> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        // Identity comes from the validated mobile token only (profile_id claim; the session was checked at token validation).
        long? profileId = _currentUser.ProfileId;
        MobileUserProfile? profile = profileId == null ? null : await _profiles.GetByIdAsync(profileId.Value, cancellationToken);
        if (profile == null || !profile.IsActive)
            return Result.Failure<MobileProfileResponseDto>(ApiErrorCodes.Unauthorized, "Please sign in again.");

        // The mobile profile id is not a PIS id: the link is the NSG number.
        UserPis? match = await _pis.GetByNsgNumberAsync(profile.NsgNumber, cancellationToken);
        UserPis? pis = match == null ? null : await _pis.GetDetailAsync(match.Id, cancellationToken);
        if (pis == null)
            return Result.Failure<MobileProfileResponseDto>(ApiErrorCodes.NotFound, PisRecordNotFoundMessage);

        return Result.Success(new MobileProfileResponseDto
        {
            // ── Identity and service ──
            NsgNumber = pis.NsgNumber,
            ServiceNumber = pis.ServiceNumber,
            FullName = pis.FullName,
            NsgRankId = pis.NsgRankId,
            NsgRankName = pis.NsgRank?.RankName,
            NsgTradeId = pis.NsgTradeId,
            NsgTradeName = pis.NsgTrade?.NsgTradeName,
            ParentDepartmentRankId = pis.ParentDepartmentRankId,
            ParentDepartmentRankName = pis.ParentDepartmentRank?.Name,
            PdTradeId = pis.PdTradeId,
            PdTradeName = pis.PdTrade?.PdTradeName,
            NsgUnitId = pis.NsgUnitId,
            NsgUnitName = pis.NsgUnit?.Name,
            EducationId = pis.EducationId,
            EducationName = pis.Education?.CourseName,
            DateOfInductionInNsg = pis.DateOfInductionInNsg,
            SideSteppedOn = pis.SideSteppedOn,
            SideSteppedFromNsgUnitId = pis.SideSteppedFromNsgUnitId,
            SideSteppedFromNsgUnitName = pis.SideSteppedFromNsgUnit?.Name,
            DateOfRepatriation = pis.DateOfRepatriation,
            DateOfBirth = pis.DateOfBirth,
            DateOfEnrollmentInGovtService = pis.DateOfEnrollmentInGovtService,
            DateOfSuperannuation = pis.DateOfSuperannuation,

            // ── Personal and family ──
            Gender = pis.Gender,
            Category = pis.Category,
            ReligionId = pis.ReligionId,
            ReligionName = pis.Religion?.Name,
            FatherName = pis.FatherName,
            MotherName = pis.MotherName,
            SpouseName = pis.SpouseName,
            NextOfKinMobileNumber = pis.NextOfKinMobileNumber,
            NumberOfChildren = pis.NumberOfChildren,
            MobileNumber = pis.MobileNumber,
            BloodGroup = pis.BloodGroup,
            HeightCm = pis.HeightCm.HasValue ? (int)pis.HeightCm.Value : null,
            WeightKg = pis.WeightKg,

            // ── Sensitive identifiers (decrypted for display; the stored ciphertext is untouched) ──
            AadhaarNumber = Reveal(pis.AadhaarNumberEncrypted, "Aadhaar"),
            PanNumber = Reveal(pis.PanNumberEncrypted, "PAN"),
            PranNumber = Reveal(pis.PranNumberEncrypted, "PRAN"),
            SalaryAccountNumber = Reveal(pis.SalaryAccountNumberEncrypted, "SalaryAccount"),

            // ── Permanent address ──
            PermanentVillage = pis.PermanentVillage,
            PermanentPostOffice = pis.PermanentPostOffice,
            PermanentPoliceStation = pis.PermanentPoliceStation,
            PermanentDistrict = pis.PermanentDistrict,
            PermanentStateId = pis.PermanentStateId,
            PermanentStateName = pis.PermanentState?.Name,
            PermanentPinCode = pis.PermanentPinCode,

            // ── Service and leave summaries ──
            DateOfAme = pis.DateOfAme,
            DateOfImmovablePropertyReturn = pis.DateOfImmovablePropertyReturn,
            EarnedLeaveDue = pis.EarnedLeaveDue,
            HalfPayLeaveDue = pis.HalfPayLeaveDue,
            PaternityLeaveAvailed = pis.PaternityLeaveAvailed,
            MaternityLeaveAvailed = pis.MaternityLeaveAvailed,
            ChildCareLeaveAvailed = pis.ChildCareLeaveAvailed
        });
    }
}
