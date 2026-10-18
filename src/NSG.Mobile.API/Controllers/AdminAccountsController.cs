using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Admin;
using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Super Admin management of Admin Portal accounts. Every endpoint requires the <c>RequireSuperAdmin</c> policy (an admin access
/// token with the SUPER_ADMIN role and a live session). The acting admin is always taken from the token, never from the request.
/// </summary>
[ApiController]
[Route("api/v1/admin/accounts")]
[Authorize(Policy = DomainConstants.Policies.RequireSuperAdmin)]
[Produces("application/json")]
public class AdminAccountsController : ControllerBase
{
    private const int MaxSearchLength = 100;

    private readonly IAdminAccountService _service;

    public AdminAccountsController(IAdminAccountService service) => _service = service;

    /// <summary>
    /// Provision an account. NSG number, mobile number and date of birth must match one active PIS record; the account is created as
    /// PendingActivation without a password and the person activates it with an OTP sent to the PIS-registered mobile.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResult<AdminAccountDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAdminAccountRequestDto request, CancellationToken cancellationToken)
    {
        if (!AdminPrincipal.TryGetProfileId(User, out long actorId)) return Unauthorized();

        Result<AdminChangeResult> result = await _service.CreateAsync(request, actorId, ClientIp(), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new ApiResult<AdminAccountDto>(result.Data!.Message, result.Data.Account))
            : MobileAuthProblem.From(this, result);
    }

    /// <summary>Page through accounts. When nothing matches, the answer is a single message instead of an empty list.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AdminAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? roleCode = null,
        [FromQuery] AdminActivationStatus? activationStatus = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (activationStatus.HasValue && !Enum.IsDefined(activationStatus.Value))
            return MobileAuthProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, "Activation status must be PendingActivation or Active."));
        if (search is { Length: > MaxSearchLength } || roleCode is { Length: > 50 })
            return MobileAuthProblem.From(this, Result.Failure(ApiErrorCodes.ValidationFailed, $"Search must not exceed {MaxSearchLength} characters."));

        PaginationRequest paging = new() { PageNumber = pageNumber, PageSize = pageSize };
        Result<PaginatedResponse<AdminAccountDto>> result = await _service.GetPagedAsync(
            paging.PageNumber, paging.PageSize, new AdminAccountFilter(roleCode?.Trim().ToUpperInvariant(), activationStatus, isActive, search), cancellationToken);

        return result.Data!.TotalCount == 0
            ? Ok(new ApiMessageResponse(AdminMessages.NoAccountsFound))
            : Ok(result.Data);
    }

    /// <summary>One account.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(AdminAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        Result<AdminAccountDto> result = await _service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Data) : MobileAuthProblem.From(this, result);
    }

    /// <summary>Deactivate an account: it cannot sign in, refresh a token or call any protected admin API, and its sessions end. Repeating the request answers with a message.</summary>
    [HttpPost("{id:long}/deactivate")]
    [ProducesResponseType(typeof(ApiResult<AdminAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(long id, CancellationToken cancellationToken)
    {
        if (!AdminPrincipal.TryGetProfileId(User, out long actorId)) return Unauthorized();
        Result<AdminChangeResult> result = await _service.DeactivateAsync(id, actorId, ClientIp(), cancellationToken);
        return result.IsSuccess ? Ok(new ApiResult<AdminAccountDto>(result.Data!.Message, result.Data.Account)) : MobileAuthProblem.From(this, result);
    }

    /// <summary>Re-enable a deactivated account (an account that never activated still has to activate). Repeating the request answers with a message.</summary>
    [HttpPost("{id:long}/reactivate")]
    [ProducesResponseType(typeof(ApiResult<AdminAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(long id, CancellationToken cancellationToken)
    {
        if (!AdminPrincipal.TryGetProfileId(User, out long actorId)) return Unauthorized();
        Result<AdminChangeResult> result = await _service.ReactivateAsync(id, actorId, ClientIp(), cancellationToken);
        return result.IsSuccess ? Ok(new ApiResult<AdminAccountDto>(result.Data!.Message, result.Data.Account)) : MobileAuthProblem.From(this, result);
    }

    /// <summary>The six fixed roles a Super Admin can assign.</summary>
    [HttpGet("~/api/v1/admin/roles")]
    [ProducesResponseType(typeof(ApiResult<IReadOnlyList<AdminRoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AdminRoleDto>> result = await _service.GetRolesAsync(cancellationToken);
        return result.Data!.Count == 0
            ? Ok(new ApiMessageResponse(AdminMessages.NoRolesFound))
            : Ok(new ApiResult<IReadOnlyList<AdminRoleDto>>(AdminMessages.RolesFound, result.Data));
    }

    // The socket address only: X-Forwarded-For is client-controlled and is not trusted for audit or throttling.
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Reads the admin identity from a validated admin access token. The id is never accepted from a request body or query.</summary>
internal static class AdminPrincipal
{
    public static bool TryGetProfileId(ClaimsPrincipal user, out long profileId)
    {
        profileId = 0;
        return long.TryParse(user.FindFirst(DomainConstants.AdminClaims.ProfileId)?.Value, out profileId) && profileId > 0;
    }
}
