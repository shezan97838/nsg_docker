using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSG.Mobile.Application.Admin;
using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Admin Portal sign-in: NSG number + password (step 1, sends the OTP to the PIS-registered mobile), then password + OTP (step 2,
/// issues tokens). A session is refreshed with its refresh token and no new OTP; signing out ends it and the next sign-in needs an OTP.
/// Public steps are anonymous and rate limited. Admin access tokens are accepted only by admin endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/auth")]
[Produces("application/json")]
public class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _service;

    public AdminAuthController(IAdminAuthService service) => _service = service;

    /// <summary>Step 1: check the password and send the OTP to the mobile number registered in PIS. No token is issued.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<AdminOtpSentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequestDto request, CancellationToken cancellationToken)
    {
        Result<AdminOtpSentDto> result = await _service.LoginAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResult<AdminOtpSentDto>(result.Data!.DemoOtpMode ? AdminMessages.DemoOtpSent : AdminMessages.OtpSent, result.Data))
            : MobileAuthProblem.From(this, result);
    }

    /// <summary>Step 2: check the password again and the OTP, then issue the access and refresh tokens.</summary>
    [HttpPost("login/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<AdminLoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyLogin([FromBody] AdminLoginVerifyRequestDto request, CancellationToken cancellationToken)
    {
        Result<AdminLoginResponseDto> result = await _service.VerifyLoginAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess ? Ok(new ApiResult<AdminLoginResponseDto>(AdminMessages.LoginSuccessful, result.Data)) : MobileAuthProblem.From(this, result);
    }

    /// <summary>Exchange a refresh token for a new access token and a new refresh token, without a new OTP. The presented token is used up; presenting it again ends the session.</summary>
    [HttpPost("token/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<AdminLoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh([FromBody] AdminRefreshRequestDto request, CancellationToken cancellationToken)
    {
        Result<AdminLoginResponseDto> result = await _service.RefreshAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess ? Ok(new ApiResult<AdminLoginResponseDto>(AdminMessages.SessionRefreshed, result.Data)) : MobileAuthProblem.From(this, result);
    }

    /// <summary>End the session of a refresh token. Always answers 200 with a message, whether or not the token was known.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout([FromBody] AdminLogoutRequestDto request, CancellationToken cancellationToken)
    {
        Result<string> result = await _service.LogoutAsync(request, ClientIp(), cancellationToken);
        return Ok(new ApiMessageResponse(result.Data!));
    }

    /// <summary>The signed-in admin's own identity and role, from the token. Open to every admin role.</summary>
    [HttpGet("me")]
    [Authorize(Policy = DomainConstants.Policies.RequireAdminAccount)]
    [ProducesResponseType(typeof(ApiResult<AdminProfileSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!AdminPrincipal.TryGetProfileId(User, out long adminProfileId)) return Unauthorized();
        Result<AdminProfileSummaryDto> result = await _service.GetMeAsync(adminProfileId, cancellationToken);
        return result.IsSuccess ? Ok(new ApiResult<AdminProfileSummaryDto>(AdminMessages.ProfileRetrieved, result.Data)) : MobileAuthProblem.From(this, result);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
