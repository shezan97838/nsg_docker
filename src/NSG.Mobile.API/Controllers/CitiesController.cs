using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Application.Geography.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Cities/towns master data.
/// Reads: any authenticated user. Writes: IT admin.
/// </summary>
[ApiController]
[Route("api/v1/cities")]
[Authorize]
[Produces("application/json")]
public class CitiesController : ControllerBase
{
    private readonly ICityService _cityService;

    public CitiesController(ICityService cityService) => _cityService = cityService;

    /// <summary>List cities/towns with search, filters, sorting and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<CityResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] CityListQuery query, CancellationToken cancellationToken)
    {
        Result<PaginatedResponse<CityResponseDto>> result = await _cityService.GetPagedAsync(query, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a city/town by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<CityResponseDto> result = await _cityService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GeographyProblem.From(this, result);
    }

    /// <summary>Create a city/town.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCityRequestDto request, CancellationToken cancellationToken)
    {
        Result<CityResponseDto> result = await _cityService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : GeographyProblem.From(this, result);
    }

    /// <summary>Update a city/town.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(typeof(CityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCityRequestDto request, CancellationToken cancellationToken)
    {
        Result<CityResponseDto> result = await _cityService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GeographyProblem.From(this, result);
    }

    /// <summary>Delete a city/town.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireITAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        Result result = await _cityService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : GeographyProblem.From(this, result);
    }
}
