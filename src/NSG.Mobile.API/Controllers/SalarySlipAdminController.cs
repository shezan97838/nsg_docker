using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.SalarySlips;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Salary Slip endpoints of the Admin Portal: bulk PDF upload and the calling admin's own upload history. Only the SALARY_MANAGEMENT_ADMIN
/// role is admitted. The acting admin always comes from the token; an admin never sees or downloads another admin's batches or slips.
/// </summary>
[ApiController]
[Route("api/v1/admin/salary-slips")]
[Authorize(Policy = DomainConstants.Policies.RequireSalaryManagementAdmin)]
[Produces("application/json")]
public class SalarySlipAdminController : ControllerBase
{
    /// <summary>Request limit: the PDF limit of <see cref="SalarySlipSettings.MaxPdfSizeBytes"/> plus form overhead.</summary>
    internal const long MaxUploadRequestBytes = 101L * 1024 * 1024;

    private readonly ISalarySlipProcessingService _processing;
    private readonly ISalarySlipAdminService _admin;

    public SalarySlipAdminController(ISalarySlipProcessingService processing, ISalarySlipAdminService admin)
    {
        _processing = processing;
        _admin = admin;
    }

    /// <summary>
    /// Uploads one multipage salary slip PDF for a salary month (<c>salaryMonth=yyyy-MM</c>). Every page is matched to a bank account by its
    /// <c>Bank A/c No</c>; one PDF is stored per account. Pages that cannot be processed are listed, never discarded.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(ApiResponse<SalarySlipUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload([FromForm] SalarySlipUploadForm form, CancellationToken cancellationToken)
    {
        if (form.File == null || form.File.Length == 0)
            return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "A PDF file is required in the form field 'file'."));
        if (!TryParseMonth(form.SalaryMonth, out DateOnly month))
            return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "salaryMonth is required in the form yyyy-MM, for example 2026-10."));

        await using Stream stream = form.File.OpenReadStream();
        Result<SalarySlipUploadResultDto> result = await _processing.ProcessAsync(new SalarySlipUploadRequest(stream, form.File.FileName, month), cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<SalarySlipUploadResultDto>(true, result.Data!.Message, result.Data))
            : Problem(result);
    }

    /// <summary>The calling admin's upload batches, newest first. Optional <c>month=yyyy-MM</c> filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<SalarySlipBatchSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBatches([FromQuery] string? month = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        DateOnly? salaryMonth = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!TryParseMonth(month, out DateOnly parsed))
                return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "month must be in the form yyyy-MM, for example 2026-10."));
            salaryMonth = parsed;
        }

        Result<PaginatedResponse<SalarySlipBatchSummaryDto>> result = await _admin.GetBatchesAsync(salaryMonth, pageNumber, pageSize, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse("No salary slip upload batches found."))
            : Ok(new ApiResponse<PaginatedResponse<SalarySlipBatchSummaryDto>>(true, "Salary slip upload batches retrieved successfully.", result.Data));
    }

    /// <summary>One of the calling admin's batches: status, page counts, unmatched page numbers and (paged) the salary slips it produced. 404 for another admin's batch.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<SalarySlipBatchDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(long id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        Result<SalarySlipBatchDetailDto> result = await _admin.GetBatchAsync(id, pageNumber, pageSize, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<SalarySlipBatchDetailDto>(true, "Salary slip upload batch retrieved successfully.", result.Data))
            : Problem(result);
    }

    /// <summary>
    /// Downloads the PDF of ONE salary slip (<c>id</c> is a <c>salarySlipId</c> from the batch detail, not a batch id). Allowed only when the
    /// slip's batch was uploaded by the calling admin; anything else is a 404. The file is streamed; no storage path is exposed.
    /// </summary>
    [HttpGet("{id:long}/download")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken = default)
    {
        Result<SalarySlipDownload> result = await _admin.OpenDownloadAsync(id, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        Response.Headers.CacheControl = "no-store";
        return File(result.Data!.Content, "application/pdf", result.Data.FileName);
    }

    private static bool TryParseMonth(string? value, out DateOnly month)
    {
        month = default;
        if (!DateTime.TryParseExact(value?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            return false;
        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    private IActionResult Problem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Unauthorized => Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        ApiErrorCodes.InternalError => StatusCode(500, new ProblemDetails { Status = 500, Title = "Internal Server Error", Detail = result.ErrorMessage }),
        _ => ResultProblem.From(this, result)
    };
}

/// <summary>Multipart form of the salary slip upload.</summary>
public class SalarySlipUploadForm
{
    /// <summary>The multipage salary slip PDF.</summary>
    public IFormFile? File { get; set; }

    /// <summary>The salary month as <c>yyyy-MM</c> (stored as its first day).</summary>
    public string? SalaryMonth { get; set; }
}
