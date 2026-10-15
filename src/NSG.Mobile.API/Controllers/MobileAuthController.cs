using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Mobile-app registration, OTP, MPIN and login. Public steps are anonymous and rate limited; they identify a person only by
/// what the server re-verifies (NSG number, mobile number, date of birth, OTP, grant). Every success body is
/// <c>{ "message": "...", "data": { "status": "...", ... } }</c>; every failure is a ProblemDetails with a <c>detail</c> message.
/// </summary>
[ApiController]
[Route("api/v1/mobile-auth")]
[Produces("application/json")]
public class MobileAuthController : ControllerBase
{
    private readonly IMobileAuthService _service;

    public MobileAuthController(IMobileAuthService service)
    {
        _service = service;
    }

    /// <summary>Decide the next step from NSG number, mobile number and date of birth: MPIN, OTP, pending, or IT support.</summary>
    [HttpPost("login/check")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Check([FromBody] MobileLoginCheckRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.CheckAsync(request, cancellationToken));

    /// <summary>Log in with the MPIN. The access token is issued only after the MPIN has been verified.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileLoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] MobileLoginRequestDto request, CancellationToken cancellationToken)
    {
        Result<MobileLoginResponseDto> result = await _service.LoginAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResult<MobileLoginResponseDto>("Login successful.", result.Data))
            : MobileAuthProblem.From(this, result);
    }

    /// <summary>Exchange a refresh token for a new access token and a new refresh token. The presented token is used up; presenting it again ends the session.</summary>
    [HttpPost("token/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileLoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh([FromBody] MobileRefreshRequestDto request, CancellationToken cancellationToken)
    {
        Result<MobileLoginResponseDto> result = await _service.RefreshAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResult<MobileLoginResponseDto>("Session refreshed.", result.Data))
            : MobileAuthProblem.From(this, result);
    }

    /// <summary>End the session of a refresh token. Always answers 200 with a message, whether or not the token was known.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout([FromBody] MobileLogoutRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.LogoutAsync(request, ClientIp(), cancellationToken));

    /// <summary>Send an OTP to the mobile number registered in PIS (or on the profile, for MPIN recovery) once the person is eligible.</summary>
    [HttpPost("otp/send")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendOtp([FromBody] MobileOtpSendRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.SendOtpAsync(request, ClientIp(), cancellationToken));

    /// <summary>Verify the OTP, then raise the approval request (PIS Pending) or issue the next grant (PIS Approved / MPIN recovery).</summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyOtp([FromBody] MobileOtpVerifyRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.VerifyOtpAsync(request, ClientIp(), cancellationToken));

    /// <summary>Submit the registration with the grant from otp/verify. Rechecks PIS; never creates a profile for a person awaiting approval.</summary>
    [HttpPost("registration/submit")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitRegistration([FromBody] RegistrationSubmitRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.SubmitRegistrationAsync(request, ClientIp(), cancellationToken));

    /// <summary>Set the initial MPIN with the grant from registration/submit and create the Employee profile.</summary>
    [HttpPost("mpin/setup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetupMpin([FromBody] MpinSetupRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.SetupMpinAsync(request, ClientIp(), cancellationToken));

    /// <summary>Change the MPIN of the signed-in user after verifying the current MPIN.</summary>
    [HttpPost("mpin/change")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<IActionResult> ChangeMpin([FromBody] MpinChangeRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.ChangeMpinAsync(request, ClientIp(), cancellationToken));

    /// <summary>Reset a forgotten MPIN with the grant from a verified recovery OTP. NSG number, mobile number and date of birth alone are never enough.</summary>
    [HttpPost("mpin/reset")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResult<MobileAuthFlowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetMpin([FromBody] MpinResetRequestDto request, CancellationToken cancellationToken)
        => FlowResponse(await _service.ResetMpinAsync(request, ClientIp(), cancellationToken));

    private IActionResult FlowResponse(Result<MobileAuthFlowDto> result)
    {
        if (!result.IsSuccess) return MobileAuthProblem.From(this, result);

        MobileAuthFlowDto dto = result.Data!;
        ApiResult<MobileAuthFlowDto> body = new(dto.Message, dto);

        // 201 only where something was created: the approval request, or the profile.
        return dto.Status is MobileAuthStatus.ApprovalRequested or MobileAuthStatus.ProfileCreated
            ? StatusCode(StatusCodes.Status201Created, body)
            : Ok(body);
    }

    // The socket address only: X-Forwarded-For is client-controlled and is not trusted for audit or throttling.
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

internal static class MobileAuthProblem
{
    public static IActionResult From(ControllerBase controller, Result result)
    {
        int status = result.ErrorCode switch
        {
            ApiErrorCodes.MobileNotMatched or ApiErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ApiErrorCodes.InvalidCredentials or ApiErrorCodes.Unauthorized
                or ApiErrorCodes.RefreshTokenInvalid or ApiErrorCodes.RefreshTokenExpired or ApiErrorCodes.RefreshTokenReuseDetected => StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ApiErrorCodes.AccountLocked => StatusCodes.Status423Locked,
            ApiErrorCodes.Conflict => StatusCodes.Status409Conflict,
            ApiErrorCodes.OtpRateLimited or ApiErrorCodes.OtpResendCooldown or ApiErrorCodes.OtpMaxAttemptsExceeded => StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.OtpSendFailed => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest
        };

        ProblemDetails problem = new() { Status = status, Title = result.ErrorCode, Detail = result.ErrorMessage };
        problem.Extensions["traceId"] = controller.HttpContext.Items["CorrelationId"]?.ToString() ?? controller.HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
