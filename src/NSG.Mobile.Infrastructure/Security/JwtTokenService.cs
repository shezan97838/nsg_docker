using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// JWT access token and opaque refresh token generation.
/// Access tokens are short-lived JWTs with minimal claims.
/// Refresh tokens are cryptographically random opaque strings, stored as SHA-256 hashes.
/// </summary>
public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;

    public JwtTokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public MobileAccessToken GenerateMobileAccessToken(MobileUserProfile profile, Guid sessionId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            // 'sub' is deliberately not a plain integer: profile ids are not users.id values, so the admin-only code that reads
            // an int user id from 'sub' (the approver of a decision) can never resolve one from a mobile token.
            new(JwtRegisteredClaimNames.Sub, $"mobile:{profile.Id}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.Role, profile.Role.ToString()),
            new(DomainConstants.MobileClaims.ProfileId, profile.Id.ToString()),
            new(DomainConstants.MobileClaims.TokenUse, DomainConstants.MobileClaims.MobileAccess),
            new(DomainConstants.MobileClaims.SessionId, sessionId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new MobileAccessToken(new JwtSecurityTokenHandler().WriteToken(token), _jwtOptions.AccessTokenExpirationMinutes * 60);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
