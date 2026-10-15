using NSG.Mobile.Application.Common;
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

    public MobileProfileService(ICurrentUserService currentUser, IMobileUserProfileRepository profiles, IUserPisRepository pis)
    {
        _currentUser = currentUser;
        _profiles = profiles;
        _pis = pis;
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
            NsgNumber = pis.NsgNumber,
            ServiceNumber = pis.ServiceNumber,
            FullName = pis.FullName,
            RankName = pis.NsgRank?.RankName,
            ParentDepartmentRankName = pis.ParentDepartmentRank?.Name,
            NsgTradeName = pis.NsgTrade?.NsgTradeName,
            NsgUnitName = pis.NsgUnit?.Name,
            Category = pis.Category?.ToString(),
            DateOfBirth = pis.DateOfBirth,
            DateOfInductionInNsg = pis.DateOfInductionInNsg,
            DateOfSuperannuation = pis.DateOfSuperannuation,
            MobileNumber = pis.MobileNumber
        });
    }
}
