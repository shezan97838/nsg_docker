using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Provides access to the authenticated user's identity from JWT claims.
/// Registered as Scoped — one instance per HTTP request.
/// Never trusts request body for identity — always derives from the token.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? UserId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)
                     ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub");
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public int? DeviceId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("device_id");
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public long? ProfileId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(DomainConstants.MobileClaims.ProfileId);
            return claim != null && long.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public long? AdminProfileId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(DomainConstants.AdminClaims.ProfileId);
            return claim != null && long.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public string? Role
        => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;

    public string? CorrelationId
        => _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();

    public bool IsAuthenticated
        => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
