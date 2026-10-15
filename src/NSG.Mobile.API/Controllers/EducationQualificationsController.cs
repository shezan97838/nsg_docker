using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.EducationQualifications.DTOs;
using NSG.Mobile.Application.EducationQualifications.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>Education qualification lookup. Reads are open to any authenticated user; writes require ITAdmin.</summary>
[ApiController]
[Route("api/v1/education-qualifications")]
[Authorize]
[Produces("application/json")]
public class EducationQualificationsController : ControllerBase
{
    private readonly IEducationQualificationService _service;

    public EducationQualificationsController(IEducationQualificationService service)
    {
        _service = service;
    }

    /// <summary>Get a paginated list of education qualifications, optionally filtered by active status and a course-name search. When nothing matches, returns 200 with a single message.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<EducationQualificationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<ApiResult<PaginatedResponse<EducationQualificationResponseDto>>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, isActive, search, cancellationToken);

        return result.Data!.Data is { } page ? Ok(page) : Ok(new ApiMessageResponse(result.Data.Message));
    }

    /// <summary>Get an education qualification by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EducationQualificationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<EducationQualificationResponseDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create an education qualification.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(ApiResult<EducationQualificationResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEducationQualificationRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<EducationQualificationResponseDto>> result = await _service.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>Update an education qualification (course name and active flag; both required). Returns 409 when deactivating a qualification that personnel use.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(ApiResult<EducationQualificationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEducationQualificationRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<EducationQualificationResponseDto>> result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>"Delete" an education qualification: deactivates it (idempotent) and answers with a message. Never hard-deletes; returns 409 while personnel use it.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        Result<ApiMessageResponse> result = await _service.DeactivateAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}
