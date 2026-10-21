using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Ranks.DTOs;
using NSG.Mobile.Application.Ranks.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// NSG rank hierarchy management for the admin portal.
/// Reads are open to any authenticated mobile/staff user or PIS Admin; writes require ITAdmin.
/// </summary>
[ApiController]
[Route("api/v1/ranks")]
[Authorize(Policy = DomainConstants.Policies.RequirePisLookupRead)]
[Produces("application/json")]
public class RankHierarchyController : ControllerBase
{
    private readonly IRankHierarchyService _rankService;

    public RankHierarchyController(IRankHierarchyService rankService)
    {
        _rankService = rankService;
    }

    /// <summary>Get active ranks ordered by SrNo, for populating dropdowns.</summary>
    /// <remarks>Not paginated: the rank list is a small, bounded lookup (22 official ranks).</remarks>
    [HttpGet("dropdown")]
    [ProducesResponseType(typeof(IReadOnlyList<RankDropdownDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDropdown(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RankDropdownDto>> result = await _rankService.GetDropdownAsync(cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a paginated list of ranks (admin view).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<RankResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<RankResponseDto>> result =
            await _rankService.GetPagedAsync(paging.PageNumber, paging.PageSize, isActive, search, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a rank by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RankResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<RankResponseDto> result = await _rankService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a rank.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(RankResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRankRequestDto request, CancellationToken cancellationToken)
    {
        Result<RankResponseDto> result = await _rankService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>Update a rank (including activate/deactivate via IsActive).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(RankResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRankRequestDto request, CancellationToken cancellationToken)
    {
        Result<RankResponseDto> result = await _rankService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>
    /// "Delete" a rank. Ranks are never hard-deleted: this deactivates the rank (idempotent) and returns 409 while any
    /// personnel record is assigned it.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        Result result = await _rankService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}
