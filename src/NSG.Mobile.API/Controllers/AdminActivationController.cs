using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSG.Mobile.Application.Admin;
using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// First-time activation of a provisioned Admin Portal account. Anonymous and rate limited: the person is identified only by what the
/// server re-verifies (an account waiting for activation, and NSG number, mobile number and date of birth equal to the PIS record).
/// The OTP is sent only to the mobile number registered in PIS.
/// </summary>
[ApiController]
[Route("api/v1/admin/activation")]
[Produces("application/json")]
public class AdminActivationController : ControllerBase
{
    private readonly IAdminActivationService _service;

    public AdminActivationController(IAdminActivationService service) => _service = service;

    /// <summary>Send the activation OTP to the PIS-registered mobile number.</summary>
    [HttpPost("otp")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(ApiResult<AdminOtpSentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendOtp([FromBody] AdminActivationOtpRequestDto request, CancellationToken cancellationToken)
    {
        Result<AdminOtpSentDto> result = await _service.SendOtpAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResult<AdminOtpSentDto>(result.Data!.DemoOtpMode ? AdminMessages.DemoOtpSent : AdminMessages.OtpSent, result.Data))
            : MobileAuthProblem.From(this, result);
    }

    /// <summary>Verify the OTP, set the password (only its hash is stored) and activate the account. The person then signs in with password and OTP.</summary>
    [HttpPost("complete")]
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Complete([FromBody] AdminActivationCompleteRequestDto request, CancellationToken cancellationToken)
    {
        Result<string> result = await _service.CompleteAsync(request, ClientIp(), cancellationToken);
        return result.IsSuccess ? Ok(new ApiMessageResponse(result.Data!)) : MobileAuthProblem.From(this, result);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
