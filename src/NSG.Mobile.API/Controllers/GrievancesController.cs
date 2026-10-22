using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Grievances of the signed-in mobile user (SOW §3.8). Mobile access tokens only (RequireMobileProfile), whatever the user's mobile role.
/// The person is always taken from the token; a grievance that is not the caller's is answered with 404, never 403, so its existence is not revealed.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
[Produces("application/json")]
public class GrievancesController : ControllerBase
{
    /// <summary>Largest accepted submission: the most files at the largest size, plus 1 MB for the text fields and multipart framing.</summary>
    internal const long MaxCreateRequestBytes = (GrievanceLimits.MaxFiles * (long)GrievanceLimits.MaxFileSizeMb + 1) * 1024L * 1024L;

    private readonly IGrievanceService _grievances;
    private readonly IGrievanceCategoryService _categories;

    public GrievancesController(IGrievanceService grievances, IGrievanceCategoryService categories)
    {
        _grievances = grievances;
        _categories = categories;
    }

    /// <summary>Active, non-deleted grievance categories by name (the submission dropdown). When there are none, the answer is a single message.</summary>
    [HttpGet("grievance-categories")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<GrievanceCategoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<GrievanceCategoryDto>> result = await _categories.GetActiveAsync(cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.Count == 0
            ? Ok(new ApiMessageResponse(GrievanceMessages.NoCategoriesFound))
            : Ok(new ApiResponse<IReadOnlyList<GrievanceCategoryDto>>(true, "Grievance categories fetched successfully", result.Data));
    }

    /// <summary>
    /// Raise a grievance: the only creation endpoint. Multipart form fields <c>categoryId</c>, <c>subject</c>, <c>note</c> (the description) and, optionally,
    /// up to 3 <c>files</c> (PDF or JPEG, at most 5 MB each; extension, content type and file signature must agree). Files go to private storage and are
    /// recorded with the grievance in one step; nothing stays in storage if the grievance cannot be recorded. The ticket number (GRV-YYYY-000001),
    /// status (Submitted) and owner are set by the server; the grievance starts unassigned.
    /// </summary>
   
    [HttpPost("grievances")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(GrievanceRateLimit.WritePolicy)]
    [RequestSizeLimit(MaxCreateRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<GrievanceDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromForm] CreateGrievanceForm form, CancellationToken cancellationToken)
    {
        List<Stream> streams = new();
        try
        {
            List<GrievanceUploadFile> uploads = new();
            foreach (IFormFile file in form.Files ?? new List<IFormFile>())
            {
                Stream stream = file.OpenReadStream();
                streams.Add(stream);
                uploads.Add(new GrievanceUploadFile(stream, file.FileName, file.ContentType, file.Length));
            }

            CreateGrievanceRequestDto request = new() { CategoryId = form.CategoryId, Subject = form.Subject, Note = form.Note };
            Result<ApiResult<GrievanceDetailDto>> result = await _grievances.CreateAsync(request, uploads, cancellationToken);
            return result.IsSuccess
                ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Data!.Id }, result.Data)
                : GrievanceProblem.From(this, result);
        }
        finally
        {
            foreach (Stream stream in streams) await stream.DisposeAsync();
        }
    }

    /// <summary>Your grievances, newest first, optionally filtered by status (1 Submitted, 2 Under Review, 3 Action Taken, 4 Closed). When none match, the answer is a single message.</summary>
    [HttpGet("grievances")]
    [ProducesResponseType(typeof(PaginatedResponse<GrievanceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] GrievanceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return GrievanceProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, "Status must be 1 (Submitted), 2 (Under Review), 3 (Action Taken) or 4 (Closed)."));

        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<GrievanceSummaryDto>> result = await _grievances.GetMineAsync(paging.PageNumber, paging.PageSize, status, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(GrievanceMessages.NoGrievancesFound))
            : Ok(result.Data);
    }

    /// <summary>One of your grievances with its documents and the complete history (creation, status changes, admin comments, forwards, resolution), each with date and actor type. 404 when it does not exist or is not yours.</summary>
    [HttpGet("grievances/{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<GrievanceDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<GrievanceDetailDto> result = await _grievances.GetMineByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<GrievanceDetailDto>(true, "Grievance fetched successfully", result.Data))
            : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Download one document of your own grievance (<c>index</c> is the zero-based position in the grievance's document list).
    /// The file is streamed through the API; there is no public or pre-signed link. 404 when the grievance is not yours.
    /// </summary>
    [HttpGet("grievances/{id:long}/documents/{index:int}")]
    [Produces("application/pdf", "image/jpeg", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadDocument(long id, int index, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _grievances.OpenMyDocumentAsync(id, index, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }
}

/// <summary>Builds the download response: attachment with the sanitised stored name, the stored validated content type, no sniffing, no caching.</summary>
internal static class GrievanceFileResult
{
    public static IActionResult From(ControllerBase controller, GrievanceDocumentContent content)
    {
        // The name was sanitised at upload; it is cleaned again here so a damaged stored value can never reach a header.
        string fileName = GrievanceDocumentRules.SanitizeFileName(content.FileName, Path.GetExtension(content.FileName));

        controller.Response.Headers["X-Content-Type-Options"] = "nosniff";
        controller.Response.Headers["Cache-Control"] = "private, no-store";

        // FileStreamResult writes Content-Disposition: attachment with an RFC 6266 encoded filename and disposes the stream after sending.
        // Range processing is off: a document is always sent whole, so there is no Accept-Ranges header and no partial-content response.
        return controller.File(content.Stream, content.ContentType, fileName, enableRangeProcessing: false);
    }
}

/// <summary>
/// Problem responses of the Grievance module. Storage and ticket-number outages are 503; everything else (400, 401, 403, 404, 409) is
/// mapped by <see cref="MobileAuthProblem"/> from the shared error codes.
/// </summary>
internal static class GrievanceProblem
{
    public static IActionResult From(ControllerBase controller, Result result)
    {
        if (result.ErrorCode is not (GrievanceErrorCodes.StorageUnavailable or GrievanceErrorCodes.TicketUnavailable))
            return MobileAuthProblem.From(controller, result);

        ProblemDetails problem = new() { Status = StatusCodes.Status503ServiceUnavailable, Title = result.ErrorCode, Detail = result.ErrorMessage };
        problem.Extensions["traceId"] = controller.HttpContext.Items["CorrelationId"]?.ToString() ?? controller.HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable, ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>Name of the rate-limit policy for grievance create and upload, registered in Program.cs.</summary>
internal static class GrievanceRateLimit
{
    public const string WritePolicy = "grievance-write";
}

/// <summary>
/// An upload larger than the request limit makes the server stop reading the body (HTTP 413, or the multipart length limit) while the form
/// is being bound, before the action runs. This filter turns that into the same 400 ProblemDetails every other upload problem produces,
/// naming the limit, instead of a generic 500.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class OversizedUploadIsBadRequestAttribute : ExceptionFilterAttribute
{
    internal const string Message = "The upload is too large. Each file may be at most 5 MB (a grievance can have at most 3 files).";

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

/// <summary>Multipart form of the grievance creation endpoint.</summary>
public class CreateGrievanceForm
{
    public int CategoryId { get; set; }
    public string Subject { get; set; } = string.Empty;

    /// <summary>The description of the grievance.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Optional attachments: PDF or JPEG.</summary>
    public List<IFormFile>? Files { get; set; }
}
