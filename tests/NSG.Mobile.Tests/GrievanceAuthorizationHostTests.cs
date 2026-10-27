using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// The Grievance module through the REAL pipeline: Kestrel-less test host, real JWT validation (both schemes), real policies, real middleware, real
/// controllers and real services. Only the outermost edges are replaced by in-memory fakes: the grievance repository, file storage, audit, and the two
/// session look-ups (so a valid session can be simulated without a database). A dummy connection string and disabled migrations guarantee that nothing
/// can reach MySQL or R2.
///
/// NOT covered: the real SQL of the repositories (including whether the session look-up really checks the role), real MySQL and real R2.
/// </summary>
[Collection("GrievanceHost")]
public sealed class GrievanceAuthorizationHostTests : IDisposable
{
    private static readonly string[] Variables =
    {
        "ConnectionStrings__MySql", "Database__MigrateOnStartup", "Security__DisableAuthorization", "Jwt__Secret",
        "Encryption__Key", "BlindIndex__Key", "ASPNETCORE_ENVIRONMENT", "RateLimiting__GrievanceWritePermitLimit"
    };

    private const long AdminId = 9001, OtherAdminId = 9002, EmployeeId = 501, OtherEmployeeId = 502;

    private readonly Dictionary<string, string?> _previous = new();
    private readonly GrvFakeRepository _repo = new();
    private readonly GrvFakeStorage _storage = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeAdminSessions _adminSessions = new();
    private readonly FakeMobileSessions _mobileSessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public GrievanceAuthorizationHostTests()
    {
        foreach (string name in Variables) _previous[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=127.0.0.1;Port=1;Database=host_check_dummy;Uid=x;Pwd=x;");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        Environment.SetEnvironmentVariable("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("BlindIndex__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("RateLimiting__GrievanceWritePermitLimit", "1000");

        _repo.ActiveGrievanceAdmins.Add(OtherAdminId);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGrievanceRepository>();
            services.AddSingleton<IGrievanceRepository>(_repo);
            services.RemoveAll<IFileStorageService>();
            services.AddSingleton<IFileStorageService>(_storage);
            services.RemoveAll<IStorageAvailability>();
            services.AddSingleton<IStorageAvailability>(new GrvAvailability());
            services.RemoveAll<IAuditService>();
            services.AddSingleton<IAuditService>(_audit);
            services.RemoveAll<IAdminRefreshTokenRepository>();
            services.AddSingleton<IAdminRefreshTokenRepository>(_adminSessions);
            services.RemoveAll<IMobileRefreshTokenRepository>();
            services.AddSingleton<IMobileRefreshTokenRepository>(_mobileSessions);
        }));
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach ((string name, string? value) in _previous) Environment.SetEnvironmentVariable(name, value);
    }

    // ── Tokens: made by the application's own token services, so claims, audience and role mapping are the production ones ──

    private string AdminToken(string roleCode, long profileId = AdminId, bool sessionActive = true, string? registeredRole = null)
    {
        Guid sid = Guid.NewGuid();
        if (sessionActive) _adminSessions.Active.Add((sid, profileId, registeredRole ?? roleCode));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAdminTokenService>()
            .GenerateAdminAccessToken(new AdminProfile { Id = profileId }, roleCode, sid).Token;
    }

    private string MobileToken(long profileId = EmployeeId, bool sessionActive = true)
    {
        Guid sid = Guid.NewGuid();
        if (sessionActive) _mobileSessions.Active.Add((sid, profileId));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateMobileAccessToken(new MobileUserProfile { Id = profileId }, sid).Token;
    }

    private string ManualToken(string audienceSuffix, DateTime notBefore, DateTime expires, byte[]? signingKey, params Claim[] claims)
    {
        IConfigurationSection jwt = _factory.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        SigningCredentials credentials = new(new SymmetricSecurityKey(signingKey ?? Encoding.UTF8.GetBytes(jwt["Secret"]!)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(jwt["Issuer"], jwt["Audience"] + audienceSuffix, claims, notBefore, expires, credentials));
    }

    private Claim[] AdminClaims(string role, Guid? sid = null) => new[]
    {
        new Claim(ClaimTypes.Role, role),
        new Claim(DomainConstants.AdminClaims.ProfileId, AdminId.ToString()),
        new Claim(DomainConstants.AdminClaims.TokenUse, DomainConstants.AdminClaims.AdminAccess),
        new Claim(DomainConstants.AdminClaims.SessionId, (sid ?? Guid.NewGuid()).ToString())
    };

    // ── Data ──

    private GrievanceCategory Category => _repo.Categories.FirstOrDefault() ?? _repo.AddCategory("Medical");

    private Grievance Seed(GrievanceStatus status, long owner = EmployeeId, bool withAttachment = false, bool withResolutionFile = false)
    {
        GrievanceDocumentRecord attachment = new("grievances/2026/10/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.pdf", "evidence.pdf", "application/pdf", GrvKit.PdfBytes.Length, new DateTime(2026, 10, 15, 11, 0, 0));
        Grievance g = _repo.AddGrievance(owner, Category, status, withAttachment ? new[] { attachment } : null);
        if (withAttachment) _storage.Objects[attachment.Key] = GrvKit.PdfBytes;
        if (withResolutionFile)
        {
            string resolutionKey = $"grievances/2026/10/{Guid.NewGuid()}.pdf";
            _repo.Grievances.Single(x => x.Id == g.Id).ResolutionDocument = resolutionKey;
            _storage.Objects[resolutionKey] = GrvKit.PdfBytes;
        }
        return g;
    }

    // ── Requests ──

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static MultipartFormDataContent ResolveForm(string? remarks, byte[]? file, string fileName = "proof.pdf", string contentType = "application/pdf")
    {
        MultipartFormDataContent form = new();
        if (remarks != null) form.Add(new StringContent(remarks), "remarks");
        if (file != null)
        {
            ByteArrayContent part = new(file);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(part, "file", fileName);
        }
        return form;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer, HttpContent? content = null)
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(method, url) { Content = content };
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>Every admin route of the module, each ready to send (new content per call).</summary>
    private static IEnumerable<(string Name, HttpMethod Method, string Url, Func<HttpContent?> Content)> AdminRoutes(long id) => new (string, HttpMethod, string, Func<HttpContent?>)[]
    {
        ("list", HttpMethod.Get, "/api/v1/admin/grievances", () => null),
        ("detail", HttpMethod.Get, $"/api/v1/admin/grievances/{id}", () => null),
        ("document", HttpMethod.Get, $"/api/v1/admin/grievances/{id}/documents/0", () => null),
        ("status", HttpMethod.Put, $"/api/v1/admin/grievances/{id}/status", () => Json("{\"toStatus\":2}")),
        ("comment", HttpMethod.Post, $"/api/v1/admin/grievances/{id}/comments", () => Json("{\"comment\":\"Checking.\"}")),
        ("forward", HttpMethod.Post, $"/api/v1/admin/grievances/{id}/forward", () => Json("{\"toAdminId\":9002}")),
        ("resolve", HttpMethod.Post, $"/api/v1/admin/grievances/{id}/resolve", () => ResolveForm("Done.", GrvKit.PdfBytes)),
        ("resolution-document", HttpMethod.Get, $"/api/v1/admin/grievances/{id}/resolution-document", () => null),
        ("dashboard", HttpMethod.Get, "/api/v1/admin/dashboard/summary", () => null),
        ("categories-list", HttpMethod.Get, "/api/v1/admin/grievance-categories", () => null),
        ("category-get", HttpMethod.Get, "/api/v1/admin/grievance-categories/1", () => null),
        ("category-create", HttpMethod.Post, "/api/v1/admin/grievance-categories", () => Json("{\"name\":\"New category\"}")),
        ("category-update", HttpMethod.Put, "/api/v1/admin/grievance-categories/1", () => Json("{\"name\":\"Renamed\",\"isActive\":true}")),
        ("category-delete", HttpMethod.Delete, "/api/v1/admin/grievance-categories/1", () => null)
    };

    private static IEnumerable<(string Name, HttpMethod Method, string Url, Func<HttpContent?> Content)> EmployeeRoutes(long id) => new (string, HttpMethod, string, Func<HttpContent?>)[]
    {
        ("categories", HttpMethod.Get, "/api/v1/grievance-categories", () => null),
        ("list", HttpMethod.Get, "/api/v1/grievances", () => null),
        ("detail", HttpMethod.Get, $"/api/v1/grievances/{id}", () => null),
        ("document", HttpMethod.Get, $"/api/v1/grievances/{id}/documents/0", () => null),
        ("resolution-document", HttpMethod.Get, $"/api/v1/grievances/{id}/resolution-document", () => null),
        ("create", HttpMethod.Post, "/api/v1/grievances", () => new MultipartFormDataContent
        {
            { new StringContent("1"), "categoryId" }, { new StringContent("Subject"), "subject" }, { new StringContent("A note that is long enough."), "note" }
        })
    };

    private async Task AssertEveryRouteAsync(IEnumerable<(string Name, HttpMethod Method, string Url, Func<HttpContent?> Content)> routes, string? bearer, HttpStatusCode expected)
    {
        foreach (var route in routes)
        {
            HttpResponseMessage response = await SendAsync(route.Method, route.Url, bearer, route.Content());
            Assert.True(response.StatusCode == expected, $"{route.Method} {route.Url} -> {(int)response.StatusCode}, expected {(int)expected}");
        }
    }

    private void AssertNothingHappened(int grievances, int logs)
    {
        Assert.Empty(_storage.StoreCalls);
        Assert.Empty(_audit.Entries);
        Assert.Equal(grievances, _repo.Grievances.Count);
        Assert.Equal(logs, _repo.Logs.Count);
        Assert.Equal(0, _repo.ResolveCalls);
        Assert.Equal(0, _repo.ChangeStatusCalls);
    }

    // ═══════════════ GRIEVANCE_ADMIN: allowed everywhere, with the identity taken from the token ═══════════════

    [Fact]
    public async Task A_grievance_admin_reads_everything_on_the_admin_side()
    {
        Grievance g = Seed(GrievanceStatus.Submitted, withAttachment: true);
        string token = AdminToken(AdminRoleCodes.GrievanceAdmin);

        foreach (string url in new[] { "/api/v1/admin/grievances", $"/api/v1/admin/grievances/{g.Id}", "/api/v1/admin/dashboard/summary",
                     "/api/v1/admin/grievance-categories", $"/api/v1/admin/grievance-categories/{Category.Id}" })
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, url, token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            using JsonDocument body = await BodyAsync(response);
            Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        }

        HttpResponseMessage download = await SendAsync(HttpMethod.Get, $"/api/v1/admin/grievances/{g.Id}/documents/0", token);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(GrvKit.PdfBytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains(_audit.Entries, e => e.Action == AuditAction.GrievanceViewed && e.Data!.Contains($"\"actorId\":{AdminId}"));
        Assert.Contains(_audit.Entries, e => e.Action == AuditAction.DocumentDownloaded && e.Data!.Contains($"\"actorId\":{AdminId}"));
    }

    [Fact]
    public async Task A_grievance_admin_works_a_grievance_from_review_to_resolution_and_every_audit_field_is_the_token_admin()
    {
        Grievance g = Seed(GrievanceStatus.Submitted);
        string token = AdminToken(AdminRoleCodes.GrievanceAdmin);

        // The bodies try to smuggle in other identities; the DTOs have no such fields and the server must use the token.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/v1/admin/grievances/{g.Id}/status", token, Json("{\"toStatus\":2,\"adminId\":7,\"changedBy\":7}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/v1/admin/grievances/{g.Id}/comments", token, Json("{\"comment\":\"Checking with the unit.\",\"changedBy\":7}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/v1/admin/grievances/{g.Id}/forward", token, Json($"{{\"toAdminId\":{OtherAdminId},\"remarks\":\"Please handle.\"}}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/v1/admin/grievances/{g.Id}/status", token, Json("{\"toStatus\":3,\"remarks\":\"Inquiry done.\",\"actionTakenBy\":7}"))).StatusCode);

        // Resolving through the status endpoint is refused: it needs the comment and the file.
        HttpResponseMessage viaStatus = await SendAsync(HttpMethod.Put, $"/api/v1/admin/grievances/{g.Id}/status", token, Json("{\"toStatus\":4,\"remarks\":\"Done.\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, viaStatus.StatusCode);

        HttpResponseMessage resolve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/grievances/{g.Id}/resolve", token, ResolveForm("Reimbursed in full.", GrvKit.PdfBytes, "proof.pdf"));
        string text = await resolve.Content.ReadAsStringAsync();
        Assert.True(resolve.StatusCode == HttpStatusCode.OK, text);
        using (JsonDocument body = JsonDocument.Parse(text))
        {
            Assert.Equal("Grievance resolved successfully.", body.RootElement.GetProperty("message").GetString());
            JsonElement data = body.RootElement.GetProperty("data");
            Assert.Equal(4, data.GetProperty("status").GetInt32());
            string reference = data.GetProperty("resolutionDocument").GetString()!;
            Assert.Equal(_storage.StoreCalls.Single(), reference);                        // only the stored reference, a plain string
            Assert.DoesNotContain("http", reference, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(_storage.StoreCalls.Single(), _repo.Grievances.Single().ResolutionDocument);   // what is stored in the column is exactly that string

        Grievance stored = _repo.Grievances.Single();
        Assert.Equal(AdminId, stored.ActionTakenBy);
        Assert.Equal(AdminId, stored.ClosedBy);
        Assert.Equal(OtherAdminId, stored.AssignedTo);
        Assert.Equal("Reimbursed in full.", stored.ClosedRemarks);
        Assert.All(_repo.Logs, l => Assert.Equal(AdminId, l.ChangedBy));
        Assert.All(_repo.Logs, l => Assert.Equal(GrievanceActorType.Admin, l.ChangedByType));
        Assert.Equal(5, _repo.Logs.Count);                                                  // review, comment, forward, action taken, resolved

        HttpResponseMessage file = await SendAsync(HttpMethod.Get, $"/api/v1/admin/grievances/{g.Id}/resolution-document", token);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(GrvKit.PdfBytes, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", file.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("attachment", file.Content.Headers.ContentDisposition!.ToString());
        Assert.Contains("resolution-GRV-", file.Content.Headers.ContentDisposition!.ToString());
    }

    [Fact]
    public async Task Category_writes_stamp_the_token_admin_never_a_value_from_the_body()
    {
        string token = AdminToken(AdminRoleCodes.GrievanceAdmin);

        HttpResponseMessage create = await SendAsync(HttpMethod.Post, "/api/v1/admin/grievance-categories", token, Json("{\"name\":\"Pay\",\"createdBy\":7,\"updatedBy\":7}"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        GrievanceCategory pay = _repo.Categories.Single(c => c.GrievanceName == "Pay");
        Assert.Equal(AdminId, pay.CreatedBy);

        HttpResponseMessage update = await SendAsync(HttpMethod.Put, $"/api/v1/admin/grievance-categories/{pay.Id}", token, Json("{\"name\":\"Pay and allowances\",\"isActive\":true,\"updatedBy\":7}"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(AdminId, pay.UpdatedBy);

        HttpResponseMessage delete = await SendAsync(HttpMethod.Delete, $"/api/v1/admin/grievance-categories/{pay.Id}", token);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.True(pay.IsDeleted);
        Assert.Equal(AdminId, pay.UpdatedBy);
    }

    [Fact]
    public async Task Resolve_validates_the_comment_and_the_file_over_the_wire_and_changes_nothing_when_they_are_wrong()
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken);
        string token = AdminToken(AdminRoleCodes.GrievanceAdmin);
        string url = $"/api/v1/admin/grievances/{g.Id}/resolve";

        (string Name, HttpContent Form, string Fragment)[] bad =
        {
            ("no comment", ResolveForm(null, GrvKit.PdfBytes), "resolution comment is required"),
            ("blank comment", ResolveForm("   ", GrvKit.PdfBytes), "resolution comment is required"),
            ("no file", ResolveForm("Done.", null), "supporting document"),
            ("exe", ResolveForm("Done.", new byte[] { 0x4D, 0x5A }, "run.exe", "application/octet-stream"), "Only PDF, JPG and PNG"),
            ("png bytes named pdf", ResolveForm("Done.", GrvKit.PngBytes, "fake.pdf", "application/pdf"), "content does not match"),
            ("content type mismatch", ResolveForm("Done.", GrvKit.PdfBytes, "a.pdf", "image/jpeg"), "content type does not match"),
            ("empty file", ResolveForm("Done.", Array.Empty<byte>(), "empty.pdf"), "empty"),
        };
        foreach ((string name, HttpContent form, string fragment) in bad)
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Post, url, token, form);
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {(int)response.StatusCode} {text}");
            using JsonDocument body = JsonDocument.Parse(text);
            Assert.False(body.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(fragment, body.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(_storage.StoreCalls);
        Assert.Equal(GrievanceStatus.ActionTaken, _repo.Grievances.Single().CurrentStatus);
        Assert.Empty(_repo.Logs);
    }

    [Fact]
    public async Task A_resolve_request_without_a_multipart_body_is_refused_and_a_storage_outage_is_a_503_that_changes_nothing()
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken);
        string token = AdminToken(AdminRoleCodes.GrievanceAdmin);
        string url = $"/api/v1/admin/grievances/{g.Id}/resolve";

        HttpResponseMessage json = await SendAsync(HttpMethod.Post, url, token, Json("{\"remarks\":\"Done.\"}"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);

        _storage.UnavailableOnStore = true;
        HttpResponseMessage outage = await SendAsync(HttpMethod.Post, url, token, ResolveForm("Done.", GrvKit.PdfBytes));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);

        Assert.Equal(GrievanceStatus.ActionTaken, _repo.Grievances.Single().CurrentStatus);
        Assert.Null(_repo.Grievances.Single().ResolutionDocument);
        Assert.Empty(_repo.Logs);
        Assert.Empty(_storage.Objects);
    }

    // ═══════════════ Everyone else is refused before any work is done ═══════════════

    [Theory]
    [InlineData(AdminRoleCodes.SuperAdmin)]
    [InlineData(AdminRoleCodes.RegistrationAdmin)]
    [InlineData(AdminRoleCodes.PisAdmin)]
    [InlineData(AdminRoleCodes.MediaCellAdmin)]
    [InlineData(AdminRoleCodes.SalaryManagementAdmin)]
    public async Task Every_other_admin_role_gets_403_on_every_admin_route_and_nothing_happens(string otherRole)
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken, withAttachment: true, withResolutionFile: false);
        string token = AdminToken(otherRole);

        await AssertEveryRouteAsync(AdminRoutes(g.Id), token, HttpStatusCode.Forbidden);

        AssertNothingHappened(grievances: 1, logs: 0);
        Assert.Equal(GrievanceStatus.ActionTaken, _repo.Grievances.Single().CurrentStatus);
        Assert.Single(_repo.Categories);                                                    // the category was not touched
        Assert.False(_repo.Categories.Single().IsDeleted);
    }

    [Fact]
    public async Task A_real_mobile_token_gets_401_on_every_admin_route_and_nothing_happens()
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken, withAttachment: true);

        await AssertEveryRouteAsync(AdminRoutes(g.Id), MobileToken(), HttpStatusCode.Unauthorized);

        AssertNothingHappened(grievances: 1, logs: 0);
    }

    [Fact]
    public async Task A_caller_without_a_token_gets_401_on_every_route_of_the_module()
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken, withAttachment: true);

        await AssertEveryRouteAsync(AdminRoutes(g.Id), null, HttpStatusCode.Unauthorized);
        await AssertEveryRouteAsync(EmployeeRoutes(g.Id), null, HttpStatusCode.Unauthorized);

        AssertNothingHappened(grievances: 1, logs: 0);
    }

    [Fact]
    public async Task Invalid_admin_tokens_are_all_401()
    {
        Grievance g = Seed(GrievanceStatus.ActionTaken);
        Guid liveSession = Guid.NewGuid();
        _adminSessions.Active.Add((liveSession, AdminId, AdminRoleCodes.GrievanceAdmin));
        DateTime now = DateTime.UtcNow;

        Dictionary<string, string> tokens = new()
        {
            ["garbage"] = "not-a-jwt",
            ["signed with another key"] = ManualToken(".Admin", now, now.AddMinutes(10), RandomNumberGenerator.GetBytes(48), AdminClaims(AdminRoleCodes.GrievanceAdmin, liveSession)),
            ["expired"] = ManualToken(".Admin", now.AddMinutes(-30), now.AddMinutes(-20), null, AdminClaims(AdminRoleCodes.GrievanceAdmin, liveSession)),
            ["mobile audience"] = ManualToken("", now, now.AddMinutes(10), null, AdminClaims(AdminRoleCodes.GrievanceAdmin, liveSession)),
            ["unknown session (logged out or replaced)"] = AdminToken(AdminRoleCodes.GrievanceAdmin, sessionActive: false),
            ["role no longer held (changed after sign-in)"] = AdminToken(AdminRoleCodes.GrievanceAdmin, registeredRole: AdminRoleCodes.PisAdmin),
            ["missing token_use claim"] = ManualToken(".Admin", now, now.AddMinutes(10), null,
                new Claim(ClaimTypes.Role, AdminRoleCodes.GrievanceAdmin), new Claim(DomainConstants.AdminClaims.ProfileId, AdminId.ToString()), new Claim(DomainConstants.AdminClaims.SessionId, liveSession.ToString())),
            ["alg none"] = UnsignedToken(".Admin", AdminClaims(AdminRoleCodes.GrievanceAdmin, liveSession)),
        };

        foreach ((string name, string token) in tokens)
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/admin/grievances", token);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {(int)response.StatusCode}");
            HttpResponseMessage change = await SendAsync(HttpMethod.Put, $"/api/v1/admin/grievances/{g.Id}/status", token, Json("{\"toStatus\":2}"));
            Assert.True(change.StatusCode == HttpStatusCode.Unauthorized, $"{name} (write): {(int)change.StatusCode}");
        }

        AssertNothingHappened(grievances: 1, logs: 0);
    }

    private string UnsignedToken(string audienceSuffix, Claim[] claims)
    {
        IConfigurationSection jwt = _factory.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        string Part(object o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Dictionary<string, object> payload = new()
        {
            ["iss"] = jwt["Issuer"]!, ["aud"] = jwt["Audience"] + audienceSuffix,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
        };
        foreach (Claim c in claims) payload[c.Type] = c.Value;
        return Part(new { alg = "none", typ = "JWT" }) + "." + Part(payload) + ".";
    }

    // ═══════════════ The employee side: own grievances only, an admin token is not accepted ═══════════════

    [Fact]
    public async Task An_admin_token_gets_401_on_every_employee_route_even_with_the_grievance_admin_role()
    {
        Grievance g = Seed(GrievanceStatus.Closed, withAttachment: true, withResolutionFile: true);

        await AssertEveryRouteAsync(EmployeeRoutes(g.Id), AdminToken(AdminRoleCodes.GrievanceAdmin), HttpStatusCode.Unauthorized);

        AssertNothingHappened(grievances: 1, logs: 0);
    }

    [Fact]
    public async Task An_employee_reads_and_downloads_only_their_own_grievance_including_the_resolution_file()
    {
        Grievance mine = Seed(GrievanceStatus.Closed, EmployeeId, withAttachment: true, withResolutionFile: true);
        Grievance theirs = Seed(GrievanceStatus.Closed, OtherEmployeeId, withAttachment: true, withResolutionFile: true);
        string token = MobileToken(EmployeeId);

        // Own: 200
        HttpResponseMessage detail = await SendAsync(HttpMethod.Get, $"/api/v1/grievances/{mine.Id}", token);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        string detailText = await detail.Content.ReadAsStringAsync();
        Assert.Contains(mine.ResolutionDocument!, detailText);                              // the employee detail returns the stored reference
        Assert.DoesNotContain(theirs.ResolutionDocument!, detailText);

        HttpResponseMessage resolution = await SendAsync(HttpMethod.Get, $"/api/v1/grievances/{mine.Id}/resolution-document", token);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        Assert.Equal(GrvKit.PdfBytes, await resolution.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", resolution.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/grievances/{mine.Id}/documents/0", token)).StatusCode);

        // Someone else's: 404 everywhere (never 403, never a download)
        foreach (string url in new[] { $"/api/v1/grievances/{theirs.Id}", $"/api/v1/grievances/{theirs.Id}/documents/0", $"/api/v1/grievances/{theirs.Id}/resolution-document" })
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, url, token);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"GET {url} -> {(int)response.StatusCode}");
        }

        // The list shows only the caller's own
        using JsonDocument list = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/grievances", token));
        JsonElement items = list.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(mine.Id, items[0].GetProperty("id").GetInt64());

        Assert.DoesNotContain(_audit.Entries, e => e.Action == AuditAction.DocumentDownloaded && e.EntityId == theirs.Id.ToString());
    }

    [Fact]
    public async Task A_grievance_without_a_resolution_file_has_nothing_to_download_for_its_owner()
    {
        Grievance open = Seed(GrievanceStatus.UnderReview, EmployeeId);
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/api/v1/grievances/{open.Id}/resolution-document", MobileToken(EmployeeId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.Equal(GrievanceMessages_ResolutionDocumentNotFound, body.RootElement.GetProperty("message").GetString());
    }

    private const string GrievanceMessages_ResolutionDocumentNotFound = "No resolution document is available for this grievance.";

    [Fact]
    public async Task A_mobile_token_whose_session_ended_is_refused_on_the_employee_routes()
    {
        Grievance mine = Seed(GrievanceStatus.Submitted, EmployeeId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/api/v1/grievances/{mine.Id}", MobileToken(EmployeeId, sessionActive: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_employee_can_still_raise_a_grievance_and_it_is_theirs()
    {
        _repo.AddCategory("Medical");

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/grievances", MobileToken(EmployeeId), new MultipartFormDataContent
        {
            { new StringContent("1"), "categoryId" }, { new StringContent("Subject"), "subject" }, { new StringContent("A note that is long enough."), "note" },
            { new StringContent("7"), "createdBy" }                                         // ignored: the owner is the token's profile
        });

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.Equal(EmployeeId, _repo.Grievances.Single().CreatedBy);
    }

    // ── Session look-ups that stand in for the database ──

    private sealed class FakeAdminSessions : IAdminRefreshTokenRepository
    {
        public HashSet<(Guid Session, long Profile, string Role)> Active { get; } = new();

        public Task<bool> IsSessionActiveAsync(Guid sessionId, long adminProfileId, string roleCode, CancellationToken cancellationToken = default)
            => Task.FromResult(Active.Contains((sessionId, adminProfileId, roleCode)));

        public Task StartSessionAsync(AdminRefreshToken first, DateTime nowIst, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, AdminRefreshToken next, DateTime nowIst, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowIst, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeAllForProfileAsync(long adminProfileId, string reason, DateTime nowIst, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMobileSessions : IMobileRefreshTokenRepository
    {
        public HashSet<(Guid Session, long Profile)> Active { get; } = new();

        public Task<bool> IsSessionActiveAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Active.Contains((sessionId, profileId)));

        public Task StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
