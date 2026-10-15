using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Onboarding.DTOs;
using NSG.Mobile.Application.Onboarding.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// IT Cell onboarding management controller.
/// SOW §3.7 Step 2: IT Cell verifies identity and approves/rejects registration.
/// 
/// All endpoints require ITAdmin role.
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
[Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
[Produces("application/json")]
public class OnboardingController : ControllerBase
{
    private readonly IOnboardingService _onboardingService;

    public OnboardingController(IOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    /// <summary>Get paginated list of pending registrations awaiting IT approval.</summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(PaginatedResponse<PendingRegistrationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingRegistrations(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _onboardingService.GetPendingRegistrationsAsync(pageNumber, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(result.ErrorMessage);
    }

    /// <summary>Approve a pending registration.</summary>
    /// <remarks>SOW §3.7 Step 2: Upon approval, the user is notified and account is activated.</remarks>
    [HttpPost("approve/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ApproveRegistration(
        int userId,
        [FromBody] ApprovalRequestDto? request,
        CancellationToken cancellationToken)
    {
        var result = await _onboardingService.ApproveRegistrationAsync(userId, request?.Reason, cancellationToken);

        if (!result.IsSuccess)
            return result.ErrorCode == ApiErrorCodes.NotFound
                ? NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = result.ErrorMessage })
                : BadRequest(new ProblemDetails { Status = 400, Title = result.ErrorCode, Detail = result.ErrorMessage });

        return NoContent();
    }

    /// <summary>Reject a pending registration.</summary>
    [HttpPost("reject/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RejectRegistration(
        int userId,
        [FromBody] ApprovalRequestDto? request,
        CancellationToken cancellationToken)
    {
        var result = await _onboardingService.RejectRegistrationAsync(userId, request?.Reason, cancellationToken);

        if (!result.IsSuccess)
            return result.ErrorCode == ApiErrorCodes.NotFound
                ? NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = result.ErrorMessage })
                : BadRequest(new ProblemDetails { Status = 400, Title = result.ErrorCode, Detail = result.ErrorMessage });

        return NoContent();
    }
}
