using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>
/// A mobile access token identifies a <see cref="MobileUserProfile"/>. These tests validate a real token the way the API's JWT
/// bearer handler does and check how the rest of the code reads it: it must never look like a <c>users</c> row (admin approver id)
/// and must satisfy only the mobile policy. No database involved.
/// </summary>
public class MobileIdentityTests
{
    private static readonly JwtOptions Jwt = new()
    {
        Secret = Convert.ToBase64String(Enumerable.Range(1, 48).Select(i => (byte)i).ToArray()),
        Issuer = "issuer", Audience = "audience", AccessTokenExpirationMinutes = 15
    };

    private static ClaimsPrincipal Validate(string token)
    {
        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true, ValidIssuer = Jwt.Issuer, ValidateAudience = true, ValidAudience = Jwt.Audience,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Secret)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        };
        return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);   // default inbound claim mapping, as in the API
    }

    private static CurrentUserService CurrentUser(ClaimsPrincipal principal)
        => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    private static string MobileToken(UserRole role = UserRole.Employee)
        => new JwtTokenService(Options.Create(Jwt)).GenerateMobileAccessToken(new MobileUserProfile { Id = 7, Role = role }, Guid.NewGuid()).Token;

    private static string StaffToken(string role)
    {
        SigningCredentials creds = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Secret)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Jwt.Issuer, Jwt.Audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "9001"), new Claim(ClaimTypes.Role, role) }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds));
    }

    [Fact]
    public void A_mobile_token_yields_a_profile_id_and_never_a_user_id()
    {
        CurrentUserService user = CurrentUser(Validate(MobileToken()));
        Assert.Equal(7L, user.ProfileId);
        Assert.Null(user.UserId);              // so it can never be recorded as the approver of a decision
        Assert.Equal("Employee", user.Role);
        Assert.True(user.IsAuthenticated);
    }

    [Fact]
    public void A_staff_token_yields_a_user_id_and_never_a_profile_id()
    {
        CurrentUserService user = CurrentUser(Validate(StaffToken("ITAdmin")));
        Assert.Equal(9001, user.UserId);
        Assert.Null(user.ProfileId);
    }

    [Fact]
    public void The_mobile_token_has_the_approved_employee_role_and_no_admin_authority()
    {
        ClaimsPrincipal principal = Validate(MobileToken());
        Assert.True(principal.IsInRole(DomainConstants.Roles.Employee));
        Assert.False(principal.IsInRole(DomainConstants.Roles.ITAdmin));
        Assert.False(principal.IsInRole(DomainConstants.Roles.SystemAdmin));
    }

    [Fact]
    public async Task Only_the_mobile_policy_accepts_a_mobile_token_and_it_rejects_staff_tokens()
    {
        // The same requirements Program.cs registers for RequireMobileProfile and RequireITAdmin.
        AuthorizationPolicy mobile = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireClaim(DomainConstants.MobileClaims.TokenUse, DomainConstants.MobileClaims.MobileAccess)
            .RequireClaim(DomainConstants.MobileClaims.ProfileId).Build();
        AuthorizationPolicy itAdmin = new AuthorizationPolicyBuilder().RequireRole(DomainConstants.Roles.ITAdmin, DomainConstants.Roles.SystemAdmin).Build();

        ClaimsPrincipal mobileUser = Validate(MobileToken());
        ClaimsPrincipal admin = Validate(StaffToken("ITAdmin"));

        Assert.True(await Evaluate(mobile, mobileUser));
        Assert.False(await Evaluate(mobile, admin));          // a staff token cannot call mpin/change
        Assert.False(await Evaluate(itAdmin, mobileUser));    // a mobile token cannot review requests
        Assert.True(await Evaluate(itAdmin, admin));
    }

    [Fact]
    public void A_token_signed_with_another_algorithm_or_key_is_rejected()
    {
        SigningCredentials wrongKey = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256);
        string forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Jwt.Issuer, Jwt.Audience,
            new[] { new Claim(DomainConstants.MobileClaims.ProfileId, "7") }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: wrongKey));
        Assert.ThrowsAny<SecurityTokenException>(() => Validate(forged));

        SigningCredentials hs512 = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Secret)), SecurityAlgorithms.HmacSha512);
        string otherAlg = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Jwt.Issuer, Jwt.Audience,
            new[] { new Claim(DomainConstants.MobileClaims.ProfileId, "7") }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: hs512));
        Assert.ThrowsAny<SecurityTokenException>(() => Validate(otherAlg));
    }

    [Fact]
    public void The_codebase_does_not_use_aspnet_core_identity()
    {
        string root = FindRoot();
        string[] sources = Directory.GetFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".csproj")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.NotEmpty(sources);
        foreach (string file in sources)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("Microsoft.AspNetCore.Identity", text);
            Assert.DoesNotContain("IdentityUser", text);
        }
    }

    private static async Task<bool> Evaluate(AuthorizationPolicy policy, ClaimsPrincipal user)
    {
        foreach (IAuthorizationRequirement requirement in policy.Requirements)
        {
            AuthorizationHandlerContext context = new(new[] { requirement }, user, null);
            foreach (IAuthorizationHandler handler in Handlers(requirement)) await handler.HandleAsync(context);
            if (!context.HasSucceeded) return false;
        }
        return true;
    }

    // The framework's built-in handlers for the requirement types used above.
    private static IEnumerable<IAuthorizationHandler> Handlers(IAuthorizationRequirement requirement)
    {
        yield return (IAuthorizationHandler)requirement;   // ClaimsAuthorizationRequirement, RolesAuthorizationRequirement and DenyAnonymous implement it themselves
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NSG.Mobile.API.sln) not found.");
    }
}
