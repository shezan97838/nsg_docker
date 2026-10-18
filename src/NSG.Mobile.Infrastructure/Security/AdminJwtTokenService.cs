using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// Admin Portal access tokens: same signing key, issuer and algorithm lock as every other token, but their own audience
/// (<see cref="JwtOptions.EffectiveAdminAudience"/>) and <c>token_use=admin_access</c>. The default scheme, which validates the
/// mobile audience, therefore rejects them, and the <c>AdminBearer</c> scheme rejects mobile tokens.
/// </summary>
public class AdminJwtTokenService : IAdminTokenService
{
    private readonly JwtOptions _jwtOptions;

    public AdminJwtTokenService(IOptions<JwtOptions> jwtOptions) => _jwtOptions = jwtOptions.Value;

    public AdminAccessToken GenerateAdminAccessToken(AdminProfile profile, string roleCode, Guid sessionId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            // Not a plain integer, so the code that reads an int staff user id from 'sub' can never resolve one from an admin token.
            new(JwtRegisteredClaimNames.Sub, $"admin:{profile.Id}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.Role, roleCode),
            new(DomainConstants.AdminClaims.ProfileId, profile.Id.ToString()),
            new(DomainConstants.AdminClaims.TokenUse, DomainConstants.AdminClaims.AdminAccess),
            new(DomainConstants.AdminClaims.SessionId, sessionId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.EffectiveAdminAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new AdminAccessToken(new JwtSecurityTokenHandler().WriteToken(token), _jwtOptions.AccessTokenExpirationMinutes * 60);
    }
}
