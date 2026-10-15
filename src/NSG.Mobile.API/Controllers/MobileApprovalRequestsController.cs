using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>IT Admin review of mobile activation requests. Every endpoint requires the ITAdmin policy.</summary>
[ApiController]
[Route("api/v1/mobile-approval-requests")]
[Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
[Produces("application/json")]
public class MobileApprovalRequestsController : ControllerBase
{
    private const int MaxSearchLength = 100;

    private readonly IMobileApprovalService _service;

    public MobileApprovalRequestsController(IMobileApprovalService service)
    {
        _service = service;
    }

    /// <summary>
    /// Page through requests, newest first, filtered by status (Pending, Approved, Rejected) and a search on NSG number or name.
    /// When nothing matches, the answer is a single message instead of an empty list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<MobileApprovalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] MobileAppActivationStatus? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return MobileAuthProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, "Status must be Pending, Approved or Rejected."));
        if (search is { Length: > MaxSearchLength })
            return MobileAuthProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, $"Search must not exceed {MaxSearchLength} characters."));

        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<MobileApprovalRequestDto>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, status, search, cancellationToken);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(MobileApprovalService.NoneFoundMessage))
            : Ok(result.Data);
    }

    /// <summary>Approve or reject a Pending request. The request and the personnel record's activation status change together or not at all.</summary>
    [HttpPut("{id:long}/decision")]
    [ProducesResponseType(typeof(ApiResult<MobileApprovalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide(long id, [FromBody] MobileApprovalDecisionRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<MobileApprovalRequestDto>> result = await _service.DecideAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);
    }
}
