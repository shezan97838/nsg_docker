using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Digital Library administration (circulars, notices, PDFs and images). Admin Portal token with the Media Cell admin role
/// (<c>RequireMediaCellAdmin</c>, the policy of DG Messages and Banners). The acting admin always comes from the token. Delete is always a soft delete.
/// </summary>
[ApiController]
[Route("api/v1/admin/digital-library")]
[Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
[Produces("application/json")]
public class DigitalLibraryAdminController : ControllerBase
{
    /// <summary>Request ceiling: the 10 MB file limit plus 1 MB for the text fields and multipart framing.</summary>
    internal const long MaxRequestBytes = 11L * 1024 * 1024;

    private readonly IDigitalLibraryAdminService _service;

    public DigitalLibraryAdminController(IDigitalLibraryAdminService service) => _service = service;

    /// <summary>
    /// Records, active and inactive, newest first. Optional filters: <c>search</c> (in the description), <c>isActive</c>, <c>isImportant</c>, <c>fileType</c> (PDF, JPG, JPEG, PNG).
    /// No match is a 200 with the message "No digital library records found."
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<DigitalLibraryAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] bool? isActive = null, [FromQuery] bool? isImportant = null,
        [FromQuery] string? fileType = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        Result<PaginatedResponse<DigitalLibraryAdminDto>> result = await _service.GetPagedAsync(
            new DigitalLibraryAdminFilter(search, isActive, isImportant, fileType), pageNumber, pageSize, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(DigitalLibraryRules.NoRecordsMessage))
            : Ok(new ApiResponse<PaginatedResponse<DigitalLibraryAdminDto>>(true, DigitalLibraryRules.RecordsRetrievedMessage, result.Data));
    }

    /// <summary>One record with its metadata, whether or not it is active. 404 when it does not exist.</summary>
    [HttpGet("{id:long}", Name = "GetDigitalLibraryRecord")]
    [ProducesResponseType(typeof(ApiResponse<DigitalLibraryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<DigitalLibraryAdminDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<DigitalLibraryAdminDto>(true, DigitalLibraryRules.RecordRetrievedMessage, result.Data))
            : Problem(result);
    }

    /// <summary>
    /// Uploads a document. Multipart form fields: <c>description</c> (required, at most 1000 characters), <c>isImportant</c> (optional, default false) and
    /// <c>file</c> (required: PDF, JPG, JPEG or PNG, at most 10 MB; the content must match the extension). The record is created active.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [DigitalLibraryOversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResponse<DigitalLibraryAdminDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromForm] DigitalLibraryCreateForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.File?.OpenReadStream();
        DigitalLibraryUploadFile? file = form.File == null || stream == null ? null : new DigitalLibraryUploadFile(stream, form.File.FileName, form.File.Length);

        Result<DigitalLibraryAdminDto> result = await _service.CreateAsync(new DigitalLibraryCreateRequest(form.Description, form.IsImportant, file), cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return CreatedAtRoute("GetDigitalLibraryRecord", new { id = result.Data!.Id },
            new ApiResponse<DigitalLibraryAdminDto>(true, DigitalLibraryRules.CreatedMessage, result.Data));
    }

    /// <summary>
    /// Updates a record. Multipart form fields: <c>description</c> and <c>isImportant</c> (both required) and an optional <c>file</c> that replaces the document.
    /// Without a file the existing one is kept. Never reactivates an inactive record.
    /// </summary>
    [HttpPut("{id:long}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [DigitalLibraryOversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResponse<DigitalLibraryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(long id, [FromForm] DigitalLibraryUpdateForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.File?.OpenReadStream();
        DigitalLibraryUploadFile? file = form.File == null || stream == null ? null : new DigitalLibraryUploadFile(stream, form.File.FileName, form.File.Length);

        Result<DigitalLibraryAdminDto> result = await _service.UpdateAsync(id, new DigitalLibraryUpdateRequest(form.Description, form.IsImportant, file), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<DigitalLibraryAdminDto>(true, DigitalLibraryRules.UpdatedMessage, result.Data))
            : Problem(result);
    }

    /// <summary>
    /// Soft delete: sets <c>isActive</c> to false. The record and its file are kept, and the record disappears from the mobile app.
    /// Repeating the request answers "already inactive".
    /// </summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(typeof(ApiResult<DigitalLibraryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        Result<ApiResult<DigitalLibraryAdminDto>> result = await _service.DeactivateAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : Problem(result);
    }

    private IActionResult Problem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Unauthorized => Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        ApiErrorCodes.InternalError => StatusCode(500, new ProblemDetails { Status = 500, Title = "Internal Server Error", Detail = result.ErrorMessage }),
        _ => ResultProblem.From(this, result)
    };
}

/// <summary>Multipart form of the Digital Library create call.</summary>
public class DigitalLibraryCreateForm
{
    /// <summary>Required, at most 1000 characters.</summary>
    public string? Description { get; set; }

    /// <summary>Optional, default false.</summary>
    public bool? IsImportant { get; set; }

    /// <summary>Required: PDF, JPG, JPEG or PNG, at most 10 MB.</summary>
    public IFormFile? File { get; set; }
}

/// <summary>Multipart form of the Digital Library update call.</summary>
public class DigitalLibraryUpdateForm
{
    /// <summary>Required, at most 1000 characters.</summary>
    public string? Description { get; set; }

    /// <summary>Required.</summary>
    public bool? IsImportant { get; set; }

    /// <summary>Optional replacement document: PDF, JPG, JPEG or PNG, at most 10 MB.</summary>
    public IFormFile? File { get; set; }
}

/// <summary>An upload above the request ceiling makes the server stop reading the body while the form is bound; this turns that into the standard 400.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class DigitalLibraryOversizedUploadIsBadRequestAttribute : ExceptionFilterAttribute
{
    internal const string Message = "The upload is too large. A Digital Library file may be at most 10 MB.";

    public override void OnException(ExceptionContext context)
    {
        bool tooLarge = context.Exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || context.Exception is InvalidDataException { Message: var m } && m.Contains("length limit", StringComparison.OrdinalIgnoreCase);
        if (!tooLarge) return;

        ProblemDetails problem = new() { Status = StatusCodes.Status400BadRequest, Title = ApiErrorCodes.ValidationFailed, Detail = Message };
        problem.Extensions["traceId"] = context.HttpContext.Items["CorrelationId"]?.ToString() ?? context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}
