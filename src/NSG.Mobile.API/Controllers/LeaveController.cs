using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Application.Leave.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// The signed-in mobile user's own leave (read only). Mobile access tokens only (RequireMobileProfile). The person is always taken from the token's
/// profile: the endpoint accepts no NSG number, so another person's record cannot be requested. There is no write endpoint for mobile users.
/// </summary>
[ApiController]
[Route("api/v1/leave")]
[Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
[Produces("application/json")]
public class LeaveController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveController(ILeaveService leave) => _leave = leave;

    /// <summary>
    /// Your leave taken in the current calendar year (IST). Answered with 404 and a message when no record has been entered for you this year yet
    /// (a missing record is deliberately not reported as zero leave: whether it means zero is an open business question, OQ-041).
    /// </summary>
    [HttpGet("my-record")]
    [ProducesResponseType(typeof(ApiResponse<MyLeaveRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyRecord(CancellationToken cancellationToken)
    {
        Result<MyLeaveRecordDto> result = await _leave.GetMyCurrentYearAsync(cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<MyLeaveRecordDto>(true, "Leave record fetched successfully", result.Data))
            : LeaveProblem.From(this, result);
    }
}
