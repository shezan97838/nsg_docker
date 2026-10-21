using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Trades.DTOs;
using NSG.Mobile.Application.Trades.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>PD trade lookup. Reads are open to any authenticated user; writes require ITAdmin.</summary>
[ApiController]
[Route("api/v1/pd-trades")]
[Authorize(Policy = DomainConstants.Policies.RequirePisLookupRead)]
[Produces("application/json")]
public class PdTradesController : ControllerBase
{
    private readonly IPdTradeService _service;

    public PdTradesController(IPdTradeService service)
    {
        _service = service;
    }

    /// <summary>Get a paginated list of PD trades, optionally filtered by active status and a name search.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<PdTradeResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<PdTradeResponseDto>> result =
            await _service.GetPagedAsync(paging.PageNumber, paging.PageSize, isActive, search, cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Get a PD trade by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PdTradeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<PdTradeResponseDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>Create a PD trade.</summary>
    [HttpPost]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(typeof(PdTradeResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePdTradeRequestDto request, CancellationToken cancellationToken)
    {
        Result<PdTradeResponseDto> result = await _service.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : ToProblem(result);
    }

    /// <summary>Update a PD trade (name and active flag; both required). Returns 409 when deactivating a trade that personnel use.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(typeof(PdTradeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePdTradeRequestDto request, CancellationToken cancellationToken)
    {
        Result<PdTradeResponseDto> result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : ToProblem(result);
    }

    /// <summary>"Delete" a PD trade: deactivates it (idempotent). Never hard-deletes; returns 409 while personnel use it.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        Result result = await _service.DeactivateAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result);
    }

    private IActionResult ToProblem(Result result) => GeographyProblem.From(this, result);
}
