using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Pis.Import;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// PIS Admin endpoints: Excel upload (insert + update, or update only), upload history, and complete personnel records with the four
/// sensitive identifiers decrypted. Only the PIS_ADMIN role is admitted. The acting admin always comes from the token.
/// (The older <c>api/v1/users-pis</c> controller is unchanged and keeps returning masked identifiers.)
/// </summary>
[ApiController]
[Route("api/v1/admin/users-pis")]
[Produces("application/json")]
public class PisAdminController : ControllerBase
{
    internal const long MaxUploadRequestBytes = 16L * 1024 * 1024;

    private readonly IPisImportService _import;
    private readonly IPisAdminQueryService _query;

    public PisAdminController(IPisImportService import, IPisAdminQueryService query)
    {
        _import = import;
        _query = query;
    }

    /// <summary>Excel upload that INSERTS new personnel and UPDATES existing ones, matched by NSG number. Partial success: valid rows are saved, rejected rows are reported.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(ApiResponse<PisUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Upload([FromForm] PisUploadForm form, CancellationToken cancellationToken)
        => RunUploadAsync(form, PisUploadType.Upsert, cancellationToken);

    /// <summary>Excel upload that ONLY UPDATES existing personnel. An unknown NSG number is rejected (NSG_NUMBER_NOT_FOUND) and never inserted.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpPost("update-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(ApiResponse<PisUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> UpdateUpload([FromForm] PisUploadForm form, CancellationToken cancellationToken)
        => RunUploadAsync(form, PisUploadType.UpdateOnly, cancellationToken);

    private async Task<IActionResult> RunUploadAsync(PisUploadForm form, PisUploadType type, CancellationToken cancellationToken)
    {
        if (form.File == null || form.File.Length == 0)
            return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "An Excel file (.xlsx) is required in the form field 'file'."));

        await using Stream stream = form.File.OpenReadStream();
        Result<PisUploadResultDto> result = await _import.UploadAsync(new PisUploadFile(stream, form.File.FileName, form.File.Length), type, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        PisUploadResultDto data = result.Data!;
        int rejected = data.Failed + data.Conflicts;
        string message = rejected == 0
            ? "PIS Excel upload processed successfully."
            : $"PIS Excel upload processed: {rejected} of {data.TotalRows} rows were rejected; the other rows were saved.";
        if (data.ErrorTrackingFailedRows.Count > 0)
            message += $" Warning: the error details of {data.ErrorTrackingFailedRows.Count} rejected row(s) could not be saved in the batch history; they are listed in this response only.";
        return Ok(new ApiResponse<PisUploadResultDto>(true, message, data));
    }

    /// <summary>The calling admin's upload batches, newest first.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("upload-batches")]
    [ProducesResponseType(typeof(ApiResponse<PisPageDto<PisUploadBatchSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBatches([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        IActionResult? invalid = CheckPaging(pageNumber, pageSize);
        if (invalid != null) return invalid;

        Result<PisPageDto<PisUploadBatchSummaryDto>> result = await _import.GetBatchesAsync(pageNumber, pageSize, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.Pagination.TotalRecords == 0
            ? Ok(new ApiMessageResponse("No upload batches found."))
            : Ok(new ApiResponse<PisPageDto<PisUploadBatchSummaryDto>>(true, "Upload batches retrieved successfully.", result.Data));
    }

    /// <summary>One of the calling admin's batches with its summary, the records it inserted/updated and its row-level errors (both paged). 404 for another admin's batch.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("upload-batches/{batchId:long}")]
    [ProducesResponseType(typeof(ApiResponse<PisUploadBatchDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(long batchId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        IActionResult? invalid = CheckPaging(pageNumber, pageSize);
        if (invalid != null) return invalid;

        Result<PisUploadBatchDetailDto> result = await _import.GetBatchAsync(batchId, pageNumber, pageSize, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<PisUploadBatchDetailDto>(true, "Upload batch retrieved successfully.", result.Data))
            : Problem(result);
    }

    /// <summary>Paginated complete personnel records assigned to the calling admin (sensitive identifiers decrypted). Ownership, filters and sorting run in the database.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PisPageDto<PisAdminRecordDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? nsgNumber = null,
        [FromQuery] string? unit = null,
        [FromQuery] string? rank = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        Result<PisPageDto<PisAdminRecordDto>> result = await _query.GetPagedAsync(pageNumber, pageSize,
            new PisAdminQuery { Search = search, NsgNumber = nsgNumber, Unit = unit, Rank = rank, IsActive = isActive, SortBy = sortBy, SortOrder = sortOrder },
            cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.Pagination.TotalRecords == 0
            ? Ok(new ApiMessageResponse("No PIS records found."))
            : Ok(new ApiResponse<PisPageDto<PisAdminRecordDto>>(true, "PIS records retrieved successfully.", result.Data));
    }

    /// <summary>Every matching personnel record assigned to the calling admin WITHOUT pagination (refused above the safeguard limit; narrow with filters).</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("all")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PisAdminRecordDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? unit = null,
        [FromQuery] string? rank = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<PisAdminRecordDto>> result = await _query.GetAllAsync(
            new PisAdminQuery { Search = search, Unit = unit, Rank = rank, IsActive = isActive, SortBy = sortBy, SortOrder = sortOrder }, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.Count == 0
            ? Ok(new ApiMessageResponse("No PIS records found."))
            : Ok(new ApiResponse<IReadOnlyList<PisAdminRecordDto>>(true, "PIS records retrieved successfully.", result.Data));
    }

    /// <summary>One complete personnel record by id. 403 for another admin's record; 404 for a missing or unassigned one.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<PisAdminRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<PisAdminRecordDto> result = await _query.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<PisAdminRecordDto>(true, "PIS record retrieved successfully.", result.Data))
            : Problem(result);
    }

    /// <summary>One complete personnel record by NSG number. 403 for another admin's record; 404 for a missing or unassigned one.</summary>
    [Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
    [HttpGet("by-nsg-number/{nsgNumber}")]
    [ProducesResponseType(typeof(ApiResponse<PisAdminRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByNsgNumber(string nsgNumber, CancellationToken cancellationToken)
    {
        Result<PisAdminRecordDto> result = await _query.GetByNsgNumberAsync(nsgNumber, cancellationToken);
        return result.IsSuccess
            ? Ok(new ApiResponse<PisAdminRecordDto>(true, "PIS record retrieved successfully.", result.Data))
            : Problem(result);
    }

    private IActionResult? CheckPaging(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
            return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "pageNumber must be 1 or greater."));
        if (pageSize < 1 || pageSize > PisAdminQueryService.MaxPageSize)
            return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, $"pageSize must be between 1 and {PisAdminQueryService.MaxPageSize}."));
        return null;
    }

    private IActionResult Problem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Unauthorized => Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        ApiErrorCodes.Forbidden => StatusCode(403, new ProblemDetails { Status = 403, Title = "Forbidden", Detail = result.ErrorMessage }),
        ApiErrorCodes.InternalError => StatusCode(500, new ProblemDetails { Status = 500, Title = "Internal Server Error", Detail = result.ErrorMessage }),
        _ => GeographyProblem.From(this, result)
    };
}

/// <summary>Multipart form of the PIS upload endpoints.</summary>
public class PisUploadForm
{
    /// <summary>The PIS Excel workbook (.xlsx).</summary>
    public IFormFile? File { get; set; }
}
