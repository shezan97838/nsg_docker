using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Application.Geography.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// States and Union Territories master data.
/// Reads: any authenticated user. Writes: IT admin.
/// </summary>
[ApiController]
[Route("api/v1/states")]
[Authorize(Policy = DomainConstants.Policies.RequirePisLookupRead)]
[Produces("application/json")]
public class StatesController : ControllerBase
{
    private readonly IStateService _stateService;
    private readonly ICityService _cityService;

    public StatesController(IStateService stateService, ICityService cityService)
    {
        _stateService = stateService;
        _cityService = cityService;
    }

    /// <summary>List states/UTs with search, filters, sorting and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<StateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] StateListQuery query, CancellationToken cancellationToken)
    {
        Result<PaginatedResponse<StateResponseDto>> result = await _stateService.GetPagedAsync(query, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a state/UT by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<StateResponseDto> result = await _stateService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>List the cities/towns of a state/UT.</summary>
    [HttpGet("{stateId:int}/cities")]
    [ProducesResponseType(typeof(PaginatedResponse<CityResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCities(int stateId, [FromQuery] CityListQuery query, CancellationToken cancellationToken)
    {
        Result<PaginatedResponse<CityResponseDto>> result = await _cityService.GetByStateAsync(stateId, query, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a state/UT.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(typeof(StateResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateStateRequestDto request, CancellationToken cancellationToken)
    {
        Result<StateResponseDto> result = await _stateService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>Update a state/UT.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(typeof(StateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateStateRequestDto request, CancellationToken cancellationToken)
    {
        Result<StateResponseDto> result = await _stateService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Delete a state/UT. Rejected with 409 while it still has cities.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        Result result = await _stateService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}

internal static class GeographyProblem
{
    public static IActionResult From(ControllerBase controller, Result result)
    {
        return result.ErrorCode switch
        {
            ApiErrorCodes.NotFound => controller.NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = result.ErrorMessage }),
            ApiErrorCodes.Conflict => controller.Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = result.ErrorMessage }),
            _ => controller.BadRequest(new ProblemDetails { Status = 400, Title = result.ErrorCode, Detail = result.ErrorMessage })
        };
    }
}
