using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Personnel (PIS) master records for the admin portal. Reading one record, creating, updating and deactivating require an Admin Portal PIS_ADMIN
/// token (<c>RequirePisAdminPortal</c>; a legacy staff token is refused) and are limited to records assigned to the calling admin: a new record
/// is assigned to its creator, ownership is never accepted from a request and never changed here (the Super Admin's transfer workflow does that).
/// The acting admin's profile id (never a legacy staff id) is what <c>CreatedBy</c>/<c>UpdatedBy</c> and the audit log record.
/// The paged list <c>GET api/v1/users-pis</c> follows the same rules (it was a temporary anonymous exception; that exception is removed).
/// An employee viewing their own record is the Profile module's concern, not this controller's.
/// </summary>
[ApiController]
[Route("api/v1/users-pis")]
[Produces("application/json")]
public class UsersPisController : ControllerBase
{
    private readonly IUserPisService _service;

    public UsersPisController(IUserPisService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get a paginated list of the personnel records assigned to the calling PIS Admin, with search and filters. Contains no sensitive identifiers.
    /// Admin Portal PIS_ADMIN only: the search, filters, paging and total are all restricted to the caller's own records in the database query.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [ProducesResponseType(typeof(PaginatedResponse<UserPisListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? nsgRank = null,
        [FromQuery] string? nsgUnit = null,
        [FromQuery] string? parentDepartmentRank = null,
        [FromQuery] string? category = null,
        [FromQuery] MobileAppActivationStatus? mobileAppActivationStatus = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        UserPisFilter filter = new()
        {
            Search = search,
            IsActive = isActive,
            NsgRank = nsgRank,
            NsgUnit = nsgUnit,
            ParentDepartmentRank = parentDepartmentRank,
            Category = category,
            MobileAppActivationStatus = mobileAppActivationStatus
        };

        Result<PaginatedResponse<UserPisListItemDto>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>
    /// Get one personnel record. Aadhaar, PAN, PRAN and salary account number are returned masked. Admin Portal PIS_ADMIN only, and only a record assigned
    /// to the calling admin: another admin's record is 403, an unassigned or missing one is 404.
    /// </summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserPisResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<UserPisResponseDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a personnel record (active, activation status Pending).</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
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
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
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
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(long id, CancellationToken cancellationToken)
    {
        Result result = await _service.DeactivateAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Status = 403, Title = "Forbidden", Detail = result.ErrorMessage }),
        ApiErrorCodes.Unauthorized => StatusCode(StatusCodes.Status401Unauthorized,
            new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        _ => ResultProblem.From(this, result)
    };
}
