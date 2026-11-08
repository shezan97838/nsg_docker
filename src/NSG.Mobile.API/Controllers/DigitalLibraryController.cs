using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Digital Library for signed-in mobile users (mobile access token, <c>RequireMobileProfile</c>): the active documents, newest first, and their files.
/// Inactive records are never listed or served. Files are streamed through the API; no storage path or public link is ever returned.
/// </summary>
[ApiController]
[Route("api/v1/digital-library")]
[Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
[Produces("application/json")]
public class DigitalLibraryController : ControllerBase
{
    private readonly IDigitalLibraryMobileService _service;

    public DigitalLibraryController(IDigitalLibraryMobileService service) => _service = service;

    /// <summary>
    /// Active documents, newest first (ties broken by id, highest first). Optional <c>isImportant</c> filter; <c>pageNumber</c> defaults to 1, <c>pageSize</c>
    /// to 20 (at most 100). Each item has id, description, isImportant, fileType, fileUrl (relative route; call it with the same bearer token) and createdAt.
    /// When there are none, the answer is a single message.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<DigitalLibraryMobileDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? isImportant = null, CancellationToken cancellationToken = default)
    {
        Result<PaginatedResponse<DigitalLibraryMobileDto>> result = await _service.GetPagedAsync(isImportant, pageNumber, pageSize, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(DigitalLibraryRules.NoRecordsMessage))
            : Ok(new ApiResponse<PaginatedResponse<DigitalLibraryMobileDto>>(true, DigitalLibraryRules.RecordsRetrievedMessage, result.Data));
    }

    /// <summary>Streams the file (PDF, JPEG or PNG) of an active record. 404 when the record is missing, inactive, or its file is unavailable.</summary>
    [HttpGet("{id:long}/file")]
    [Produces("application/pdf", "image/jpeg", "image/png", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(long id, CancellationToken cancellationToken)
    {
        Result<DigitalLibraryDownload> result = await _service.OpenFileAsync(id, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data!.Content, result.Data.ContentType, result.Data.FileName, enableRangeProcessing: false);
    }

    private IActionResult Problem(Result result) => ResultProblem.From(this, result);
}
