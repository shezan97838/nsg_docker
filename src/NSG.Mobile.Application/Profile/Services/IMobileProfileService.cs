using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Profile.DTOs;

namespace NSG.Mobile.Application.Profile.Services;

/// <summary>Profile of the signed-in mobile user. The person is always taken from the validated token, never from the request.</summary>
public interface IMobileProfileService
{
    /// <summary>
    /// Unauthorized when the token carries no mobile profile or the profile no longer exists or is inactive;
    /// NotFound only when the profile's personnel (PIS) record does not exist.
    /// </summary>
    Task<Result<MobileProfileResponseDto>> GetMyProfileAsync(CancellationToken cancellationToken = default);
}
