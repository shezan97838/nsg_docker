using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Form 16 (income tax certificate) endpoints. Admin Portal: upload and look up records, SALARY_MANAGEMENT_ADMIN only. Each action carries its own
/// authorization attribute because the admin and mobile routes use different policies.
/// </summary>
[ApiController]
[Produces("application/json")]
public class Form16Controller : ControllerBase
{
    /// <summary>Request limit: two PDFs plus form overhead (each PDF is limited by <see cref="Form16Settings.MaxPdfSizeBytes"/>, 10 MB by default).</summary>
    internal const long MaxUploadRequestBytes = 25L * 1024 * 1024;

    private readonly IForm16AdminService _admin;
    private readonly IForm16MobileService _mobile;

    public Form16Controller(IForm16AdminService admin, IForm16MobileService mobile)
    {
        _admin = admin;
        _mobile = mobile;
    }

    // ───────────────────────── Admin Portal ─────────────────────────

    /// <summary>
    /// Uploads Part A, Part B or both for an NSG number and assessment year. Creates the record when there is none (201); otherwise updates
    /// only the supplied parts and keeps the other one (200). The NSG number must exist in PIS.
    /// </summary>
    [HttpPost("api/v1/admin/form16")]
    [Authorize(Policy = DomainConstants.Policies.RequireSalaryManagementAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [Form16OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResponse<Form16AdminDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<Form16AdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Upload([FromForm] Form16UploadForm form, CancellationToken cancellationToken)
        => SaveAsync(form, create: true, cancellationToken);

    /// <summary>Replaces the supplied parts of an existing record and keeps the other one. 404 when the record does not exist; never creates one.</summary>
    [HttpPut("api/v1/admin/form16")]
    [Authorize(Policy = DomainConstants.Policies.RequireSalaryManagementAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [Form16OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResponse<Form16AdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Update([FromForm] Form16UploadForm form, CancellationToken cancellationToken)
        => SaveAsync(form, create: false, cancellationToken);

    /// <summary>Form 16 records, newest assessment year first. Optional filters <c>nsgNumber</c> and <c>assessmentYear</c> (yyyy-yy). No match is a 200 with the message "No Form 16 records found."</summary>
    [HttpGet("api/v1/admin/form16")]
    [Authorize(Policy = DomainConstants.Policies.RequireSalaryManagementAdmin)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<Form16AdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] string? nsgNumber = null, [FromQuery] string? assessmentYear = null,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        Result<PaginatedResponse<Form16AdminDto>> result = await _admin.GetPagedAsync(nsgNumber, assessmentYear, pageNumber, pageSize, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse("No Form 16 records found."))
            : Ok(new ApiResponse<PaginatedResponse<Form16AdminDto>>(true, "Form 16 records retrieved successfully.", result.Data));
    }

    /// <summary>One Form 16 record: employee, assessment year, which parts are available and when they were uploaded.</summary>
    [HttpGet("api/v1/admin/form16/{id:long}")]
    [Authorize(Policy = DomainConstants.Policies.RequireSalaryManagementAdmin)]
    [ProducesResponseType(typeof(ApiResponse<Form16AdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<Form16AdminDto> result = await _admin.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<Form16AdminDto>(true, "Form 16 record retrieved successfully.", result.Data))
            : Problem(result);
    }

    // ───────────────────────── Mobile (the signed-in person's own Form 16) ─────────────────────────
    // Mobile access tokens only (RequireMobileProfile). The person is resolved from the token; no endpoint accepts an NSG number.

    /// <summary>Your Form 16: the assessment years that have a document, newest first, with Part A / Part B availability. When there is none the answer is 200 with the message "No Form 16 documents found."</summary>
    [HttpGet("api/v1/mobile/form16")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<Form16MobileDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<Form16MobileDto>> result = await _mobile.GetMyAsync(cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.Count == 0
            ? Ok(new ApiResponse<IReadOnlyList<Form16MobileDto>>(true, "No Form 16 documents found.", result.Data))
            : Ok(new ApiResponse<IReadOnlyList<Form16MobileDto>>(true, "Form 16 documents retrieved successfully.", result.Data));
    }

    /// <summary>Availability and upload timestamps of your Form 16 for one assessment year (yyyy-yy). 404 when there is none.</summary>
    [HttpGet("api/v1/mobile/form16/{assessmentYear}")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [ProducesResponseType(typeof(ApiResponse<Form16MobileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMineByYear(string assessmentYear, CancellationToken cancellationToken = default)
    {
        Result<Form16MobileDto> result = await _mobile.GetMyDetailsAsync(assessmentYear, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<Form16MobileDto>(true, "Form 16 retrieved successfully.", result.Data))
            : Problem(result);
    }

    /// <summary>Streams your Form 16 Part A PDF. Ownership is verified on every request; an unavailable document is a 404.</summary>
    [HttpGet("api/v1/mobile/form16/{assessmentYear}/part-a")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DownloadPartA(string assessmentYear, CancellationToken cancellationToken = default)
        => DownloadAsync(assessmentYear, Form16Part.PartA, cancellationToken);

    /// <summary>Streams your Form 16 Part B PDF. Ownership is verified on every request; an unavailable document is a 404.</summary>
    [HttpGet("api/v1/mobile/form16/{assessmentYear}/part-b")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DownloadPartB(string assessmentYear, CancellationToken cancellationToken = default)
        => DownloadAsync(assessmentYear, Form16Part.PartB, cancellationToken);

    private async Task<IActionResult> DownloadAsync(string assessmentYear, Form16Part part, CancellationToken cancellationToken)
    {
        Result<Form16Download> result = await _mobile.OpenMyPartAsync(assessmentYear, part, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data!.Content, "application/pdf", result.Data.FileName, enableRangeProcessing: false);
    }

    private async Task<IActionResult> SaveAsync(Form16UploadForm form, bool create, CancellationToken cancellationToken)
    {
        await using Stream? partA = form.PartA is { Length: > 0 } ? form.PartA.OpenReadStream() : null;
        await using Stream? partB = form.PartB is { Length: > 0 } ? form.PartB.OpenReadStream() : null;

        Form16UploadRequest request = new(form.NsgNumber, form.AssessmentYear,
            partA == null ? null : new Form16UploadFile(partA, form.PartA!.Length),
            partB == null ? null : new Form16UploadFile(partB, form.PartB!.Length));

        Result<Form16SaveOutcome> result = create
            ? await _admin.UpsertAsync(request, cancellationToken)
            : await _admin.UpdateAsync(request, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        ApiResponse<Form16AdminDto> body = new(true, result.Data!.Message, result.Data.Record);
        return result.Data.Created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    private IActionResult Problem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Unauthorized => Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        ApiErrorCodes.InternalError => StatusCode(500, new ProblemDetails { Status = 500, Title = "Internal Server Error", Detail = result.ErrorMessage }),
        _ => ResultProblem.From(this, result)
    };
}

/// <summary>Multipart form of the Form 16 upload (POST and PUT).</summary>
public class Form16UploadForm
{
    /// <summary>NSG number of the person; must exist in PIS.</summary>
    public string? NsgNumber { get; set; }

    /// <summary>Assessment year as <c>yyyy-yy</c>, for example <c>2026-27</c>.</summary>
    public string? AssessmentYear { get; set; }

    /// <summary>Part A PDF (optional).</summary>
    public IFormFile? PartA { get; set; }

    /// <summary>Part B PDF (optional).</summary>
    public IFormFile? PartB { get; set; }
}

/// <summary>An upload above the request ceiling makes the server stop reading the body while the form is bound; this turns that into the standard 400.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class Form16OversizedUploadIsBadRequestAttribute : ExceptionFilterAttribute
{
    internal const string Message = "The upload is too large. Each Form 16 PDF may be at most 10 MB.";

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
