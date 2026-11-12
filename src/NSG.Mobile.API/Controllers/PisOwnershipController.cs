using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Super Admin transfer of a personnel record to a PIS Admin (changes <c>users_pis.assigned_to</c> only). Admin Portal SUPER_ADMIN only
/// (<c>RequireSuperAdmin</c>): PIS Admin, Registration Admin, every other role, staff tokens and anonymous callers are refused. This is the
/// whole of the Super Admin's PIS responsibility: it grants no read, update, delete, upload or approval access to personnel data.
/// </summary>
[ApiController]
[Route("api/v1/admin/pis-personnel")]
[Authorize(Policy = DomainConstants.Policies.RequireSuperAdmin)]
[Produces("application/json")]
public class PisOwnershipController : ControllerBase
{
    private readonly IPisOwnershipTransferService _service;
    private readonly IPisBulkOwnershipTransferService _bulkService;

    public PisOwnershipController(IPisOwnershipTransferService service, IPisBulkOwnershipTransferService bulkService)
    {
        _service = service;
        _bulkService = bulkService;
    }

    /// <summary>
    /// Make the target PIS Admin responsible for one personnel record. Works for an unassigned record and for one owned by another PIS Admin.
    /// Pending registration requests of the person follow the new owner automatically. Repeating the request for the current owner answers with a
    /// message and changes nothing.
    /// </summary>
    [HttpPost("transfer-ownership")]
    [ProducesResponseType(typeof(ApiResult<PisOwnershipTransferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Transfer([FromBody] PisOwnershipTransferRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<PisOwnershipTransferDto>> result = await _service.TransferAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);
    }

    /// <summary>
    /// Move every personnel record currently assigned to the source PIS Admin to the target PIS Admin. The source admin's active/inactive
    /// status does not block the transfer (supports admin retirement or account deactivation). If the source has no assigned records,
    /// a successful response with zero transferred records is returned.
    /// </summary>
    [HttpPost("bulk-transfer-ownership")]
    [ProducesResponseType(typeof(ApiResult<PisBulkOwnershipTransferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BulkTransfer([FromBody] PisBulkOwnershipTransferRequestDto request, CancellationToken cancellationToken)
    {
        Result<ApiResult<PisBulkOwnershipTransferDto>> result = await _bulkService.BulkTransferAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);
    }
}
