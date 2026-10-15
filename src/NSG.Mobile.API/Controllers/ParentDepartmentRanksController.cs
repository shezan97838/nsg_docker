using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Lookups.DTOs;
using NSG.Mobile.Application.Lookups.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Parent department rank lookup management. Reads are open to any authenticated user; writes require ITAdmin.
/// </summary>
[ApiController]
[Route("api/v1/parent-department-ranks")]
[Authorize]
[Produces("application/json")]
public class ParentDepartmentRanksController : ControllerBase
{
    private readonly IParentDepartmentRankService _service;

    public ParentDepartmentRanksController(IParentDepartmentRankService service)
    {
        _service = service;
    }

    /// <summary>Get active parent department ranks ordered by name, for populating dropdowns.</summary>
    [HttpGet("dropdown")]
    [ProducesResponseType(typeof(IReadOnlyList<LookupDropdownDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDropdown(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<LookupDropdownDto>> result = await _service.GetDropdownAsync(cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a paginated list of parent department ranks, optionally filtered by active status and name search.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<LookupResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<LookupResponseDto>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, isActive, search, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a parent department rank by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LookupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<LookupResponseDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a parent department rank.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(LookupResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateLookupRequestDto request, CancellationToken cancellationToken)
    {
        Result<LookupResponseDto> result = await _service.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>Update a parent department rank (including activate/deactivate via IsActive).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(LookupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLookupRequestDto request, CancellationToken cancellationToken)
    {
        Result<LookupResponseDto> result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Delete a parent department rank. Returns 409 when personnel records still reference it.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        Result result = await _service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}
