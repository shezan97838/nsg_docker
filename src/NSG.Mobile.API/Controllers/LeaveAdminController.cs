using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Application.Leave.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Leave maintenance by Leave Admins (Admin Portal token with the LEAVE_ADMIN role; no other role, not even Super Admin, is admitted).
/// The year is always the server's current IST calendar year and the acting admin always comes from the token; neither can be sent in a request.
/// Leave taken is the final TOTAL for the year, not an increment. Responses use the global envelope.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = DomainConstants.Policies.RequireLeaveAdmin)]
[Produces("application/json")]
public class LeaveAdminController : ControllerBase
{
    /// <summary>Hard ceiling of an upload request (the configured file limit, default 2 MB, is checked by the service and must stay below this).</summary>
    internal const long MaxUploadRequestBytes = 16L * 1024 * 1024;

    private readonly ILeaveService _leave;
    private readonly ILeaveBulkUploadService _uploads;

    public LeaveAdminController(ILeaveService leave, ILeaveBulkUploadService uploads)
    {
        _leave = leave;
        _uploads = uploads;
    }

    /// <summary>
    /// Sets the TOTAL leave taken by a person in the current year: creates the record when there is none (201), otherwise replaces the stored total
    /// (200). The NSG number must belong to an active person in the personnel records. Writing the stored value again changes nothing.
    /// </summary>
    [HttpPost("leave-records")]
    [ProducesResponseType(typeof(ApiResult<LeaveRecordAdminDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResult<LeaveRecordAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromBody] SaveLeaveRecordRequestDto request, CancellationToken cancellationToken)
    {
        Result<LeaveSaveResponse> result = await _leave.SaveAsync(request.NsgNumber, request.LeaveTaken, createIfMissing: true, cancellationToken);
        if (!result.IsSuccess) return LeaveProblem.From(this, result);

        return result.Data!.Created
            ? CreatedAtAction(nameof(GetCurrentYear), new { nsgNumber = result.Data.Body.Data!.NsgNumber }, result.Data.Body)
            : Ok(result.Data.Body);
    }

    /// <summary>Replaces the TOTAL leave of a person for the current year. The record must already exist (404 otherwise; use POST to create it).</summary>
    [HttpPut("leave-records/{nsgNumber}")]
    [ProducesResponseType(typeof(ApiResult<LeaveRecordAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string nsgNumber, [FromBody] UpdateLeaveRecordRequestDto request, CancellationToken cancellationToken)
    {
        Result<LeaveSaveResponse> result = await _leave.SaveAsync(nsgNumber, request.LeaveTaken, createIfMissing: false, cancellationToken);
        return result.IsSuccess ? Ok(result.Data!.Body) : LeaveProblem.From(this, result);
    }

    /// <summary>The current-year leave record of a person. 404 with a message when the person or the record does not exist.</summary>
    [HttpGet("leave-records/{nsgNumber}")]
    [ProducesResponseType(typeof(ApiResponse<LeaveRecordAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentYear(string nsgNumber, CancellationToken cancellationToken)
    {
        Result<LeaveRecordAdminDto> result = await _leave.GetCurrentYearAsync(nsgNumber, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<LeaveRecordAdminDto>(true, "Leave record fetched successfully", result.Data))
            : LeaveProblem.From(this, result);
    }

    /// <summary>Every change of a person's leave counts (manual and bulk), newest first. When there is none, the answer is a single message.</summary>
    [HttpGet("leave-records/{nsgNumber}/history")]
    [ProducesResponseType(typeof(PaginatedResponse<LeaveHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(string nsgNumber, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<LeaveHistoryDto>> result = await _leave.GetHistoryAsync(nsgNumber, paging.PageNumber, paging.PageSize, cancellationToken);
        if (!result.IsSuccess) return LeaveProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(LeaveMessages.NoHistoryFound)) : Ok(result.Data);
    }

    /// <summary>
    /// Bulk upload of leave totals from an Excel workbook (multipart field <c>file</c>, .xlsx, first sheet, columns <c>NSGNumber</c> and <c>LeaveTaken</c>).
    /// The whole file is validated first; if any row is invalid nothing is changed and the row-level report is kept under the returned batch. A valid
    /// file is applied in one transaction (all or nothing). Only one upload runs at a time (409 otherwise).
    /// </summary>
    [HttpPost("leave-uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [LeaveOversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResponse<LeaveUploadBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload([FromForm] LeaveUploadForm form, CancellationToken cancellationToken)
    {
        if (form.File == null || form.File.Length == 0)
            return LeaveProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, LeaveMessages.FileRequired));

        await using Stream stream = form.File.OpenReadStream();
        Result<LeaveUploadBatchDto> result = await _uploads.UploadAsync(new LeaveUploadFile(stream, form.File.FileName, form.File.Length), cancellationToken);
        if (!result.IsSuccess) return LeaveProblem.From(this, result);

        LeaveUploadBatchDto batch = result.Data!;
        return Ok(new ApiResponse<LeaveUploadBatchDto>(true,
            $"Leave upload completed: {batch.CreatedCount} created, {batch.UpdatedCount} updated, {batch.UnchangedCount} unchanged.", batch));
    }

    /// <summary>Upload batches, newest first, optionally filtered by status (1 Processing, 2 Completed, 3 ValidationFailed, 4 ProcessingFailed).</summary>
    [HttpGet("leave-uploads")]
    [ProducesResponseType(typeof(PaginatedResponse<LeaveUploadBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBatches([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, [FromQuery] LeaveUploadStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return LeaveProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, "Status must be 1 (Processing), 2 (Completed), 3 (ValidationFailed) or 4 (ProcessingFailed)."));

        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<LeaveUploadBatchDto>> result = await _uploads.GetBatchesAsync(paging.PageNumber, paging.PageSize, status, cancellationToken);
        if (!result.IsSuccess) return LeaveProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(LeaveMessages.NoBatchesFound)) : Ok(result.Data);
    }

    /// <summary>One upload batch with its status and counters (use it to check an upload whose outcome was reported as uncertain).</summary>
    [HttpGet("leave-uploads/{batchId:long}")]
    [ProducesResponseType(typeof(ApiResponse<LeaveUploadBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(long batchId, CancellationToken cancellationToken)
    {
        Result<LeaveUploadBatchDto> result = await _uploads.GetBatchAsync(batchId, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<LeaveUploadBatchDto>(true, "Leave upload fetched successfully", result.Data))
            : LeaveProblem.From(this, result);
    }

    /// <summary>The row-level report of an upload batch (Excel row numbers, header = row 1). When the batch has none, the answer is a single message.</summary>
    [HttpGet("leave-uploads/{batchId:long}/errors")]
    [ProducesResponseType(typeof(PaginatedResponse<LeaveUploadErrorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatchErrors(long batchId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<LeaveUploadErrorDto>> result = await _uploads.GetErrorsAsync(batchId, paging.PageNumber, paging.PageSize, cancellationToken);
        if (!result.IsSuccess) return LeaveProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(LeaveMessages.NoErrorsFound)) : Ok(result.Data);
    }
}

/// <summary>Multipart form of the bulk upload endpoint.</summary>
public class LeaveUploadForm
{
    /// <summary>The Excel workbook (.xlsx).</summary>
    public IFormFile? File { get; set; }
}

/// <summary>Maps a service <see cref="Result"/> of the Leave module to an RFC 7807 body (then wrapped in the envelope by the global filter).</summary>
internal static class LeaveProblem
{
    public static IActionResult From(ControllerBase controller, Result result)
    {
        int? status = result.ErrorCode switch
        {
            LeaveErrorCodes.ProcessingFailed or LeaveErrorCodes.OutcomeUnknown => StatusCodes.Status500InternalServerError,
            ApiErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
            _ => null
        };
        if (status == null) return MobileAuthProblem.From(controller, result);

        ProblemDetails problem = new() { Status = status, Title = result.ErrorCode, Detail = result.ErrorMessage };
        problem.Extensions["traceId"] = controller.HttpContext.Items["CorrelationId"]?.ToString() ?? controller.HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>An upload above the request ceiling makes the server stop reading the body while the form is bound; this turns that into the standard 400.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LeaveOversizedUploadIsBadRequestAttribute : ExceptionFilterAttribute
{
    internal const string Message = "The upload is too large. Upload a single .xlsx workbook of at most 2 MB.";

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
