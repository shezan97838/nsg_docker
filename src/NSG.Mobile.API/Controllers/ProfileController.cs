using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Profile.DTOs;
using NSG.Mobile.Application.Profile.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Profile of the signed-in mobile user. SOW §3.1 (PIS): display personal details.
/// Mobile access tokens only (RequireMobileProfile: token_use = mobile_access with a profile_id, session checked on every request).
/// The person is derived from the token — there is no id or NSG number in the route, query or body, so no IDOR is possible.
/// </summary>
[ApiController]
[Route("api/v1/profile")]
[Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
[Produces("application/json")]
public class ProfileController : ControllerBase
{
    private readonly IMobileProfileService _service;

    public ProfileController(IMobileProfileService service)
    {
        _service = service;
    }

    /// <summary>Get the basic PIS details of the current mobile user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MobileProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        Result<MobileProfileResponseDto> result = await _service.GetMyProfileAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);
    }
}
