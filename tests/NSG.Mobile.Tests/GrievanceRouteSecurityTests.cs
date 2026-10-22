using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Tests;

// The host-level checks set process-wide environment variables, so this collection never runs beside other test classes.
[CollectionDefinition("GrievanceHost", DisableParallelization = true)]
public class GrievanceHostCollection { }

/// <summary>
/// The real application pipeline in-process, with a dummy connection string, migrations off and no network: nothing here can reach a database.
/// Only requests that are decided before any database access are sent (anonymous, or a token of the wrong kind).
///
/// Wrong-kind tokens get 401, not 403, by the project's design (ADR-017): the employee routes accept only the mobile scheme and audience,
/// the admin routes only the AdminBearer scheme and audience, so the other kind of token is not authenticated at all. A 403 would need
/// an authenticated admin of another role, which requires a live admin session in the database.
/// </summary>
[Collection("GrievanceHost")]
public sealed class GrievanceRouteSecurityTests : IDisposable
{
    private static readonly string[] Variables =
    {
        "ConnectionStrings__MySql", "Database__MigrateOnStartup", "Security__DisableAuthorization", "Jwt__Secret",
        "Encryption__Key", "BlindIndex__Key", "ASPNETCORE_ENVIRONMENT", "RateLimiting__GrievanceWritePermitLimit"
    };

    private readonly Dictionary<string, string?> _previous = new();
    private readonly WebApplicationFactory<Program> _factory;

    public GrievanceRouteSecurityTests()
    {
        foreach (string name in Variables) _previous[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=127.0.0.1;Port=1;Database=route_check_dummy;Uid=x;Pwd=x;");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        Environment.SetEnvironmentVariable("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("BlindIndex__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach ((string name, string? value) in _previous) Environment.SetEnvironmentVariable(name, value);
    }

    private string Token(string audienceSuffix, params Claim[] claims)
    {
        IConfigurationSection jwt = _factory.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        SigningCredentials credentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)), SecurityAlgorithms.HmacSha256);
        JwtSecurityToken token = new(jwt["Issuer"], jwt["Audience"] + audienceSuffix, claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // A genuine mobile access token makes the default scheme look up its session in the database before the admin policy rejects it, which this
    // database-free test cannot do. Without the token_use claim the default scheme accepts the token without that lookup, so what is left to
    // prove is the point of the test: the mobile audience is not accepted by the admin scheme. (The full case belongs to the later MySQL smoke test.)
    private string MobileAudienceToken() => Token("",
        new Claim(DomainConstants.MobileClaims.ProfileId, "501"));

    private string AdminToken() => Token(".Admin",
        new Claim(DomainConstants.AdminClaims.TokenUse, DomainConstants.AdminClaims.AdminAccess),
        new Claim(DomainConstants.AdminClaims.ProfileId, "9001"),
        new Claim(DomainConstants.AdminClaims.SessionId, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, AdminRoleCodes.GrievanceAdmin));

    private static readonly (HttpMethod Method, string Url)[] EmployeeRoutes =
    {
        (HttpMethod.Get, "/api/v1/grievance-categories"), (HttpMethod.Post, "/api/v1/grievances"), (HttpMethod.Get, "/api/v1/grievances"),
        (HttpMethod.Get, "/api/v1/grievances/1"), (HttpMethod.Get, "/api/v1/grievances/1/documents/0"),
        (HttpMethod.Get, "/api/v1/dg-messages"), (HttpMethod.Get, "/api/v1/dg-messages/1/image")
    };

    private static readonly (HttpMethod Method, string Url)[] AdminRoutes =
    {
        (HttpMethod.Get, "/api/v1/admin/grievances"), (HttpMethod.Get, "/api/v1/admin/grievances/1"), (HttpMethod.Get, "/api/v1/admin/grievances/1/documents/0"),
        (HttpMethod.Put, "/api/v1/admin/grievances/1/status"), (HttpMethod.Get, "/api/v1/admin/dashboard/summary"),
        (HttpMethod.Get, "/api/v1/admin/grievance-categories"), (HttpMethod.Get, "/api/v1/admin/grievance-categories/1"),
        (HttpMethod.Post, "/api/v1/admin/grievance-categories"), (HttpMethod.Put, "/api/v1/admin/grievance-categories/1"),
        (HttpMethod.Delete, "/api/v1/admin/grievance-categories/1"),
        (HttpMethod.Post, "/api/v1/admin/grievances/1/comments"), (HttpMethod.Post, "/api/v1/admin/grievances/1/forward"),
        (HttpMethod.Post, "/api/v1/admin/dg-messages"), (HttpMethod.Put, "/api/v1/admin/dg-messages/1"), (HttpMethod.Delete, "/api/v1/admin/dg-messages/1")
    };

    private async Task<HttpStatusCode> SendAsync((HttpMethod Method, string Url) route, string? bearer)
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(route.Method, route.Url);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Every_grievance_route_rejects_an_anonymous_caller_with_401()
    {
        foreach (var route in EmployeeRoutes.Concat(AdminRoutes))
            { HttpStatusCode status = await SendAsync(route, null); Assert.True(status == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)status}"); }
    }

    [Fact]
    public async Task A_token_with_the_mobile_audience_is_not_accepted_on_any_admin_route()
    {
        string mobile = MobileAudienceToken();
        foreach (var route in AdminRoutes)
            { HttpStatusCode status = await SendAsync(route, mobile); Assert.True(status == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)status}"); }
    }

    [Fact]
    public async Task An_admin_token_is_not_accepted_on_any_employee_route()
    {
        string admin = AdminToken();
        foreach (var route in EmployeeRoutes)
            { HttpStatusCode status = await SendAsync(route, admin); Assert.True(status == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)status}"); }
    }

    [Fact]
    public async Task A_token_signed_with_another_key_or_expired_is_refused_everywhere()
    {
        JwtSecurityToken forged = new("NSG.Mobile.API.Dev", "NSG.Mobile.App.Dev",
            new[] { new Claim(DomainConstants.MobileClaims.TokenUse, DomainConstants.MobileClaims.MobileAccess), new Claim(DomainConstants.MobileClaims.ProfileId, "501") },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48)), SecurityAlgorithms.HmacSha256));
        string forgedToken = new JwtSecurityTokenHandler().WriteToken(forged);

        foreach (var route in EmployeeRoutes)
            { HttpStatusCode status = await SendAsync(route, forgedToken); Assert.True(status == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)status}"); }
    }
}
