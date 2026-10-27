using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Grievance Admin work (SOW §3.8): review, document access, status changes (take action / resolve), comments, forwarding, the dashboard and the category master.
/// Every endpoint requires the RequireGrievanceAdmin policy (Admin Portal token, GRIEVANCE_ADMIN role). The acting admin is always taken from the token.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = DomainConstants.Policies.RequireGrievanceAdmin)]
[Produces("application/json")]
public class GrievanceAdminController : ControllerBase
{
    /// <summary>Largest accepted resolve request: the largest file plus 1 MB for the comment and multipart framing.</summary>
    internal const long MaxResolveRequestBytes = (GrievanceLimits.MaxFileSizeMb + 1L) * 1024L * 1024L;

    private readonly IGrievanceAdminService _grievances;
    private readonly IGrievanceCategoryService _categories;
    private readonly IGrievanceDashboardService _dashboard;

    public GrievanceAdminController(IGrievanceAdminService grievances, IGrievanceCategoryService categories, IGrievanceDashboardService dashboard)
    {
        _grievances = grievances;
        _categories = categories;
        _dashboard = dashboard;
    }

    // ── Grievances ──

    /// <summary>
    /// Page through all grievances, newest first. Filters: status, category, created between <c>fromDate</c> and <c>toDate</c> (IST days, inclusive),
    /// ticket-number search and assigned officer. Each row names the employee (NSG number, name, unit). When nothing matches, the answer is a single message.
    /// </summary>
    [HttpGet("grievances")]
    [ProducesResponseType(typeof(PaginatedResponse<GrievanceAdminSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] GrievanceStatus? status = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] long? assignedTo = null,
        CancellationToken cancellationToken = default)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return GrievanceProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, "Status must be 1 (Submitted), 2 (Under Review), 3 (Action Taken) or 4 (Closed)."));

        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<GrievanceAdminSummaryDto>> result = await _grievances.GetPagedAsync(
            paging.PageNumber, paging.PageSize, status, categoryId, fromDate, toDate, search, assignedTo, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(GrievanceMessages.NoGrievancesFound))
            : Ok(result.Data);
    }

    /// <summary>Full detail of a grievance: employee, documents and the status timeline. Each view is audited.</summary>
    [HttpGet("grievances/{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<GrievanceAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<GrievanceAdminDetailDto> result = await _grievances.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<GrievanceAdminDetailDto>(true, "Grievance fetched successfully", result.Data))
            : GrievanceProblem.From(this, result);
    }

    /// <summary>Download any grievance document (<c>index</c> is zero-based). Streamed through the API; every download is audited.</summary>
    [HttpGet("grievances/{id:long}/documents/{index:int}")]
    [Produces("application/pdf", "image/jpeg", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadDocument(long id, int index, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _grievances.OpenDocumentAsync(id, index, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Move a grievance one step forward: 1→2, 2→3 (remarks required). Resolving (3→4) is <c>POST grievances/{id}/resolve</c>, which also needs a supporting file; a 4 here is a 400. Anything else is a 400. The change and its status-log row are one transaction;
    /// the acting admin and the time are set by the server. 409 when someone else changed the grievance first.
    /// </summary>
    [HttpPut("grievances/{id:long}/status")]
    [ProducesResponseType(typeof(ApiResult<GrievanceAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateGrievanceStatusRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<GrievanceAdminDetailDto>> result = await _grievances.UpdateStatusAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Resolve a grievance that is in Action Taken (status 3 → 4 Closed). Multipart form: <c>remarks</c> (the resolution comment, required, at most 1000 characters)
    /// and <c>file</c> (the supporting document: one PDF, JPEG or PNG, at most 5 MB; extension, content type and file signature must agree). The file goes to
    /// private storage and is recorded with the status change, the comment and the history row in one transaction; nothing stays in storage if that fails.
    /// The acting admin and the time are set by the server. The employee downloads the file with <c>GET api/v1/grievances/{id}/resolution-document</c>.
    /// </summary>
    [HttpPost("grievances/{id:long}/resolve")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(GrievanceRateLimit.WritePolicy)]
    [RequestSizeLimit(MaxResolveRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<GrievanceAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Resolve(long id, [FromForm] ResolveGrievanceForm form, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            GrievanceUploadFile? upload = null;
            if (form.File != null)
            {
                stream = form.File.OpenReadStream();
                upload = new GrievanceUploadFile(stream, form.File.FileName, form.File.ContentType, form.File.Length);
            }

            Result<ApiResult<GrievanceAdminDetailDto>> result = await _grievances.ResolveAsync(id, new ResolveGrievanceRequestDto { Remarks = form.Remarks }, upload, cancellationToken);
            return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
        }
        finally
        {
            if (stream != null) await stream.DisposeAsync();
        }
    }

    /// <summary>Download the supporting file uploaded when the grievance was resolved. Streamed through the API; every download is audited. 404 when there is none.</summary>
    [HttpGet("grievances/{id:long}/resolution-document")]
    [Produces("application/pdf", "image/jpeg", "image/png", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadResolutionDocument(long id, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _grievances.OpenResolutionDocumentAsync(id, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Add a comment to the grievance history (visible to the employee). The status does not change. 409 when the grievance is closed.
    /// The acting admin and the time are set by the server.
    /// </summary>
    [HttpPost("grievances/{id:long}/comments")]
    [ProducesResponseType(typeof(ApiResult<GrievanceAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddComment(long id, [FromBody] AddGrievanceCommentRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<GrievanceAdminDetailDto>> result = await _grievances.AddCommentAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Forward or reassign the grievance to another active Grievance Admin (<c>toAdminId</c>). The status does not change; the forward is recorded in
    /// the history with the optional remarks. 409 when the grievance is closed or already assigned to that admin.
    /// </summary>
    [HttpPost("grievances/{id:long}/forward")]
    [ProducesResponseType(typeof(ApiResult<GrievanceAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Forward(long id, [FromBody] ForwardGrievanceRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<GrievanceAdminDetailDto>> result = await _grievances.ForwardAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    // ── Dashboard ──


    /// <summary>
    /// Counts by status (all four, with the pending total for statuses 1-3), by category and by month, and the average resolution time. Optional <c>fromDate</c>/<c>toDate</c> limit by creation day (IST).
    /// </summary>
    [HttpGet("dashboard/summary")]
    [ProducesResponseType(typeof(ApiResponse<GrievanceDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboardSummary([FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        Result<GrievanceDashboardDto> result = await _dashboard.GetSummaryAsync(fromDate, toDate, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(GrievanceMessages.NoDashboardData))
            : Ok(new ApiResponse<GrievanceDashboardDto>(true, "Dashboard summary fetched successfully", result.Data));
    }

    // ── Categories ──

    /// <summary>Page through grievance categories (active and inactive, not deleted), optionally by active state and name search. When none match, the answer is a single message.</summary>
    [HttpGet("grievance-categories")]
    [ProducesResponseType(typeof(PaginatedResponse<GrievanceCategoryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<GrievanceCategoryAdminDto>> result = await _categories.GetPagedAsync(paging.PageNumber, paging.PageSize, isActive, search, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(GrievanceMessages.NoCategoriesFound))
            : Ok(result.Data);
    }

    /// <summary>One category (404 when it does not exist or is deleted).</summary>
    [HttpGet("grievance-categories/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<GrievanceCategoryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategoryById(int id, CancellationToken cancellationToken)
    {
        Result<GrievanceCategoryAdminDto> result = await _categories.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<GrievanceCategoryAdminDto>(true, "Grievance category fetched successfully", result.Data))
            : GrievanceProblem.From(this, result);
    }

    /// <summary>Create a category (name, optional department, active flag; active by default). A name that already exists, even on a deactivated or deleted category, is a 409.</summary>
    [HttpPost("grievance-categories")]
    [ProducesResponseType(typeof(ApiResult<GrievanceCategoryAdminDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<GrievanceCategoryAdminDto>> result = await _categories.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetCategoryById), new { id = result.Data!.Data!.Id }, result.Data)
            : GrievanceProblem.From(this, result);
    }

    /// <summary>Replace a category's name, department and active flag (all required except departmentId). Existing grievances keep pointing at the category.</summary>
    [HttpPut("grievance-categories/{id:int}")]
    [ProducesResponseType(typeof(ApiResult<GrievanceCategoryAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<GrievanceCategoryAdminDto>> result = await _categories.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>Soft-delete a category: it disappears from the employee dropdown and the admin list, the row and the grievances that use it stay. Repeating the request answers "already deleted".</summary>
    [HttpDelete("grievance-categories/{id:int}")]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
    {
        Result<ApiMessageResponse> result = await _categories.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }
}

/// <summary>Multipart form of the resolve endpoint.</summary>
public class ResolveGrievanceForm
{
    /// <summary>The resolution comment. Required.</summary>
    public string? Remarks { get; set; }

    /// <summary>The supporting document: one PDF, JPEG or PNG. Required.</summary>
    public IFormFile? File { get; set; }
}
