namespace NSG.Mobile.Application.Interfaces.Services;

/// <summary>
/// Provides access to the currently authenticated user's identity.
/// Derived from the JWT token claims — never from request body.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Authenticated user ID from JWT 'sub' claim.</summary>
    int? UserId { get; }

    /// <summary>Mobile-app profile id from the 'profile_id' claim of a mobile access token; null for any other token.</summary>
    long? ProfileId { get; }

    /// <summary>Admin-portal account id from the 'admin_profile_id' claim of an admin access token; null for any other token.</summary>
    long? AdminProfileId { get; }

    /// <summary>Authenticated device ID from JWT claim.</summary>
    int? DeviceId { get; }

    /// <summary>User's role from JWT claim.</summary>
    string? Role { get; }

    /// <summary>Request correlation ID for distributed tracing.</summary>
    string? CorrelationId { get; }

    /// <summary>Whether the current request is authenticated.</summary>
    bool IsAuthenticated { get; }
}
