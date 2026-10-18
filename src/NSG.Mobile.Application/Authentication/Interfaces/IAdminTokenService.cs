using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Authentication.Interfaces;

/// <summary>A signed admin access token and its lifetime in seconds.</summary>
public sealed record AdminAccessToken(string Token, int ExpiresInSeconds);

/// <summary>
/// Issues Admin Portal access tokens. Kept apart from <see cref="ITokenService"/> so mobile token issuing is untouched: admin
/// tokens carry their own audience and <c>token_use</c>, so neither kind is accepted where the other is required.
/// Opaque refresh tokens and their hashing are reused from <see cref="ITokenService"/>.
/// </summary>
public interface IAdminTokenService
{
    /// <summary>Short-lived JWT with minimal claims (admin profile id, role code, token use, session id). Called only after password and OTP succeeded.</summary>
    AdminAccessToken GenerateAdminAccessToken(AdminProfile profile, string roleCode, Guid sessionId);
}
