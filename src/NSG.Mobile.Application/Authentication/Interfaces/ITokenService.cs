using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Authentication.Interfaces;

/// <summary>A signed mobile access token and its lifetime in seconds.</summary>
public sealed record MobileAccessToken(string Token, int ExpiresInSeconds);

/// <summary>
/// Token service abstraction for JWT access token generation and refresh token management.
/// Infrastructure implements this with actual JWT signing.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generate a short-lived JWT access token for an authenticated mobile-app profile. Minimal claims (profile id, role, token use, session id). Called only after a successful MPIN check.
    /// </summary>
    MobileAccessToken GenerateMobileAccessToken(MobileUserProfile profile, Guid sessionId);

    /// <summary>Generate a cryptographically secure opaque refresh token.</summary>
    string GenerateRefreshToken();

    /// <summary>Compute a SHA-256 hash of a refresh token for secure storage.</summary>
    string HashToken(string token);
}
