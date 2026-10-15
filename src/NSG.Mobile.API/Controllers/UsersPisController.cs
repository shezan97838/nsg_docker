using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Personnel (PIS) master records for the admin portal. Every endpoint requires ITAdmin: the data covers all employees
/// and includes sensitive identifiers (returned masked only). An employee viewing their own record is the Profile
/// module's concern, not this controller's.
/// </summary>
[ApiController]
[Route("api/v1/users-pis")]
[Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
[Produces("application/json")]
public class UsersPisController : ControllerBase
{
    private readonly IUserPisService _service;

    public UsersPisController(IUserPisService service)
    {
        _service = service;
    }

    /// <summary>Get a paginated list of personnel records, with search and filters. Contains no sensitive identifiers.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PaginatedResponse<UserPisListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int? nsgRankId = null,
        [FromQuery] int? nsgUnitId = null,
        [FromQuery] int? parentDepartmentRankId = null,
        [FromQuery] PersonnelCategory? category = null,
        [FromQuery] MobileAppActivationStatus? mobileAppActivationStatus = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        UserPisFilter filter = new()
        {
            Search = search,
            IsActive = isActive,
            NsgRankId = nsgRankId,
            NsgUnitId = nsgUnitId,
            ParentDepartmentRankId = parentDepartmentRankId,
            Category = category,
            MobileAppActivationStatus = mobileAppActivationStatus
        };

        Result<PaginatedResponse<UserPisListItemDto>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, filter, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get one personnel record. Aadhaar, PAN, PRAN and salary account number are returned masked.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserPisResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<UserPisResponseDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a personnel record (active, activation status Pending).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserPisResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        Result<UserPisResponseDto> result = await _service.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>
    /// Update a personnel record. The NSG number and activation status cannot be changed. Sensitive identifiers are
    /// replaced only when supplied.
    /// </summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(UserPisResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        Result<UserPisResponseDto> result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>
    /// "Delete" a personnel record. Records are never hard-deleted: this deactivates the record (idempotent).
    /// </summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(long id, CancellationToken cancellationToken)
    {
        Result result = await _service.DeactivateAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}
