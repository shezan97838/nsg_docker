using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Banners;
using NSG.Mobile.Application.Banners.DTOs;
using NSG.Mobile.Application.Banners.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Banners. Employees (mobile token) read the active banners, in priority order, and their private images; Media Cell
/// admins (Admin Portal token, <c>MEDIA_CELL_ADMIN</c>) create, update, read and activate/deactivate. The acting admin always comes from the token.
/// Images are uploaded with the banner to private storage (R2) and streamed back through authorized endpoints; only the object key is stored.
/// Responses use the global envelope.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class BannersController : ControllerBase
{
    /// <summary>Largest accepted write request: the image ceiling plus 1 MB for the text fields and multipart framing.</summary>
    internal const long MaxRequestBytes = (BannerLimits.ImageMaxMegabytes + 1) * 1024L * 1024L;

    private readonly IBannerService _service;

    public BannersController(IBannerService service) => _service = service;

    /// <summary>
    /// Active banners ordered by priority (lowest number first, banners without a priority last, then newest). Each item has id, title, description,
    /// category, imageUrl (an absolute URL, null for a text-only banner; load it with the same bearer token), imageAltText, nsgUnitId, priority, isActive, createdAt and updatedAt. When there are none, the answer is a single message.
    /// </summary>
    [HttpGet("banners")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [ProducesResponseType(typeof(PaginatedResponse<BannerDisplayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDisplay([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<BannerDisplayDto>> result = await _service.GetDisplayAsync(paging.PageNumber, paging.PageSize, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(BannerLimits.NoneActiveMessage)) : Ok(result.Data);
    }

    /// <summary>
    /// The image of an active banner, streamed through the API (the bucket is private; there is no
    /// public or pre-signed link). Send the same bearer token as for the other calls. 404 when the banner is not shown or has no image.
    /// public or pre-signed link). Send the same bearer token as for the other calls. 404 when the banner is inactive or has no image.
    [HttpGet("banners/{id:int}/image")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [Produces("image/jpeg", "image/png", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _service.OpenDisplayImageAsync(id, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }

    /// <summary>Every banner, including inactive and expired ones, newest first (paged). When there are none, the answer is a single message.</summary>
    [HttpGet("admin/banners")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [ProducesResponseType(typeof(PaginatedResponse<BannerAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<BannerAdminDto>> result = await _service.GetAllAsync(paging.PageNumber, paging.PageSize, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(BannerLimits.NoneFoundMessage)) : Ok(result.Data);
    }

    /// <summary>One banner with its schedule and state, whether or not it is currently shown.</summary>
    [HttpGet("admin/banners/{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [ProducesResponseType(typeof(BannerAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        Result<BannerAdminDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>The image of any banner (active or not, current or expired) for the Media Cell admin to preview, streamed like the employee image.</summary>
    [HttpGet("admin/banners/{id:int}/image")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [Produces("image/jpeg", "image/png", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAdminImage(int id, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _service.OpenAdminImageAsync(id, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Create a banner. Multipart form fields: <c>title</c>, <c>description</c>, <c>category</c> and <c>isActive</c> (required); <c>priority</c> (optional, lower = earlier, omitted = last),
    /// <c>imageAltText</c>, <c>showOnlyImage</c> (true only with an image, otherwise 400), <c>nsgUnitId</c> (saved for future targeting only) and an optional
    /// <c>image</c> (JPEG or PNG, at most 5 MB). The image goes to private storage and only its key is saved; without one the banner is text only.
    /// </summary>
    [HttpPost("admin/banners")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<BannerAdminDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromForm] BannerForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.Image?.OpenReadStream();
        Result<ApiResult<BannerAdminDto>> result = await _service.CreateAsync(ToRequest(form), ToUpload(form, stream), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Data!.Id }, result.Data)
            : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Replace the fields of a banner (<c>isActive</c> is required). Send an <c>image</c> to replace the picture, or
    /// <c>removeImage=true</c> to make the banner text only; with neither, the existing image is kept. The replaced or removed object is deleted from storage.
    /// </summary>
    [HttpPut("admin/banners/{id:int}")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<BannerAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(int id, [FromForm] BannerForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.Image?.OpenReadStream();
        Result<ApiResult<BannerAdminDto>> result = await _service.UpdateAsync(id, ToRequest(form), ToUpload(form, stream), cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>Activate or deactivate a banner. Repeating the request answers "already active/inactive".</summary>
    [HttpPatch("admin/banners/{id:int}/status")]
    [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [ProducesResponseType(typeof(ApiResult<BannerAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateBannerStatusRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<BannerAdminDto>> result = await _service.SetStatusAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    private static SaveBannerRequestDto ToRequest(BannerForm form) => new()
    {
        Title = form.Title, Description = form.Description, Category = form.Category, ImageAltText = form.ImageAltText, NsgUnitId = form.NsgUnitId,
        Priority = form.Priority, IsActive = form.IsActive, ShowOnlyImage = form.ShowOnlyImage, RemoveImage = form.RemoveImage
    };

    private static BannerImageUpload? ToUpload(BannerForm form, Stream? stream)
        => form.Image == null || stream == null ? null : new BannerImageUpload(stream, form.Image.FileName, form.Image.ContentType, form.Image.Length);
}

/// <summary>Multipart form of the banner create and update calls.</summary>
public class BannerForm
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Alternative text of the image (optional).</summary>
    public string? ImageAltText { get; set; }

    /// <summary>Saved for future targeting; not used for filtering (optional).</summary>
    public int? NsgUnitId { get; set; }

    /// <summary>Optional. The lower the number, the earlier the banner is listed; omitted sorts last.</summary>
    public int? Priority { get; set; }

    /// <summary>Required.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Optional. true = show the image alone; only allowed when the banner has an image (400 otherwise). Omitted: false on create, unchanged on update; removing the image sets false.</summary>
    public bool? ShowOnlyImage { get; set; }

    /// <summary>Update only: true removes the existing image (not allowed together with <c>image</c>).</summary>
    public bool? RemoveImage { get; set; }

    /// <summary>Optional JPEG or PNG image, at most 5 MB.</summary>
    public IFormFile? Image { get; set; }
}
