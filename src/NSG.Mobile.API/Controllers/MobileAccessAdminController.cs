using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAccess;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// PIS Admin management of a person's mobile access, addressed by NSG number: look the person up, block mobile access (ends every
/// session at once), restore it (the person must then log in again). Admin Portal authentication with the PIS_ADMIN role only
/// (<c>RequirePisAdminPortal</c>): neither the staff Bearer path nor any other admin role is admitted. The acting admin always comes
/// from the token. A blocked person receives the code <c>SURAKSHA_1001</c> from the mobile endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/mobile-access")]
[Authorize(Policy = DomainConstants.Policies.RequirePisAdminPortal)]
[Produces("application/json")]
public class MobileAccessAdminController : ControllerBase
{
    private readonly IMobileAccessAdminService _service;

    public MobileAccessAdminController(IMobileAccessAdminService service) => _service = service;

    /// <summary>The person's current mobile access status: identity (name, rank, unit, masked mobile), profile existence, active/blocked state, last login, lockout, active session and block details.</summary>
    [HttpGet("{nsgNumber}")]
    [ProducesResponseType(typeof(ApiResult<MobileAccessStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string nsgNumber, CancellationToken cancellationToken)
        => Respond(await _service.GetAsync(nsgNumber, cancellationToken));

    /// <summary>Block mobile access: sets the profile inactive with the block marker and revokes every active session. Repeating the request answers with a message.</summary>
    [HttpPut("{nsgNumber}/block")]
    [ProducesResponseType(typeof(ApiResult<MobileAccessStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Block(string nsgNumber, [FromBody] MobileAccessBlockRequestDto? request = null, CancellationToken cancellationToken = default)
        => Respond(await _service.BlockAsync(nsgNumber, request?.Reason, ClientIp(), cancellationToken));

    /// <summary>Restore mobile access that was blocked through this API. Old sessions stay revoked; the person logs in again. Repeating the request answers with a message.</summary>
    [HttpPut("{nsgNumber}/restore")]
    [ProducesResponseType(typeof(ApiResult<MobileAccessStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(string nsgNumber, [FromBody] MobileAccessRestoreRequestDto? request = null, CancellationToken cancellationToken = default)
        => Respond(await _service.RestoreAsync(nsgNumber, request?.Reason, ClientIp(), cancellationToken));

    private IActionResult Respond(Result<ApiResult<MobileAccessStatusDto>> result)
        => result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);

    // The socket address only: X-Forwarded-For is client-controlled and is not trusted for audit or throttling.
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
