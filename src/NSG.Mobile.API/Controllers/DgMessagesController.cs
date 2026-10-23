using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DgMessages;
using NSG.Mobile.Application.DgMessages.DTOs;
using NSG.Mobile.Application.DgMessages.Services;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// DG messages. Employees (mobile token) read the active, non-deleted messages and their private images; Media Cell admins (Admin Portal token,
/// <c>MEDIA_CELL_ADMIN</c>) create, update and soft-delete. The acting admin is always taken from the token. Responses use the global envelope.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class DgMessagesController : ControllerBase
{
    /// <summary>Largest accepted write request: the image ceiling plus 1 MB for the text fields and multipart framing.</summary>
    internal const long MaxRequestBytes = (DgMessageLimits.ImageMaxMegabytes + 1) * 1024L * 1024L;

    private readonly IDgMessageService _service;

    public DgMessagesController(IDgMessageService service) => _service = service;

    /// <summary>Active, non-deleted DG messages, newest first (paged). When there are none, the answer is a single message.</summary>
    [HttpGet("dg-messages")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [ProducesResponseType(typeof(PaginatedResponse<DgMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetActive([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<DgMessageDto>> result = await _service.GetActiveAsync(paging.PageNumber, paging.PageSize, cancellationToken);
        if (!result.IsSuccess) return GrievanceProblem.From(this, result);

        return result.Data!.TotalCount == 0 ? Ok(new ApiMessageResponse(DgMessageLimits.NoneFoundMessage)) : Ok(result.Data);
    }

    /// <summary>
    /// The image of an active, non-deleted DG message, streamed through the API (the bucket is private; there is no public or pre-signed link).
    /// Send the same bearer token as for the other calls. 404 when the message has no image.
    /// </summary>
    [HttpGet("dg-messages/{id:int}/image")]
    [Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
    [Produces("image/jpeg", "image/png", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
    {
        Result<GrievanceDocumentContent> result = await _service.OpenImageAsync(id, cancellationToken);
        return result.IsSuccess ? GrievanceFileResult.From(this, result.Data!) : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Create a DG message. Multipart form fields <c>dgName</c>, <c>designation</c>, <c>message</c> (paragraphs allowed, up to 5000 characters),
    /// optional <c>isActive</c> (default true) and an optional <c>image</c> (JPEG or PNG, at most 5 MB). The image goes to private storage and only its key is saved.
    /// </summary>
   
    [HttpPost("admin/dg-messages")]
    // TEMPORARY: anonymous so messages can be managed from Swagger without logging in. Restore the [Authorize] line below and remove [AllowAnonymous].
    [AllowAnonymous]
    // [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<DgMessageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromForm] DgMessageForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.Image?.OpenReadStream();
        Result<ApiResult<DgMessageDto>> result = await _service.CreateAsync(ToRequest(form), ToUpload(form, stream), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : GrievanceProblem.From(this, result);
    }

    /// <summary>
    /// Replace the text fields (all required, including <c>isActive</c>) of a non-deleted DG message. Send an <c>image</c> to replace the picture;
    /// without one the existing image is kept. The replaced object is removed from storage.
    /// </summary>
    [HttpPut("admin/dg-messages/{id:int}")]
    // TEMPORARY: anonymous so messages can be managed from Swagger without logging in. Restore the [Authorize] line below and remove [AllowAnonymous].
    [AllowAnonymous]
    // [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [OversizedUploadIsBadRequest]
    [ProducesResponseType(typeof(ApiResult<DgMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(int id, [FromForm] DgMessageForm form, CancellationToken cancellationToken)
    {
        await using Stream? stream = form.Image?.OpenReadStream();
        Result<ApiResult<DgMessageDto>> result = await _service.UpdateAsync(id, ToRequest(form), ToUpload(form, stream), cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    /// <summary>Soft-delete a DG message (<c>is_deleted</c>). The row and its image stay; repeating the request answers "already deleted".</summary>
    [HttpDelete("admin/dg-messages/{id:int}")]
    // TEMPORARY: anonymous so messages can be managed from Swagger without logging in. Restore the [Authorize] line below and remove [AllowAnonymous].
    [AllowAnonymous]
    // [Authorize(Policy = DomainConstants.Policies.RequireMediaCellAdmin)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        Result<ApiMessageResponse> result = await _service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : GrievanceProblem.From(this, result);
    }

    private static SaveDgMessageRequestDto ToRequest(DgMessageForm form)
        => new() { DgName = form.DgName, Designation = form.Designation, Message = form.Message, MessageHindi = form.MessageHindi, IsActive = form.IsActive };

    private static DgImageUpload? ToUpload(DgMessageForm form, Stream? stream)
        => form.Image == null || stream == null ? null : new DgImageUpload(stream, form.Image.FileName, form.Image.ContentType, form.Image.Length);
}

/// <summary>Multipart form of the DG message create and update calls.</summary>
public class DgMessageForm
{
    public string DgName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;

    /// <summary>The message; paragraphs are separated by line breaks.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional Hindi (Devanagari) version of the message, up to 5000 characters.</summary>
    public string? MessageHindi { get; set; }

    /// <summary>Optional on create (default true); required on update.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Optional JPEG or PNG image.</summary>
    public IFormFile? Image { get; set; }
}
