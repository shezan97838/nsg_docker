using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

// Form 16 through the REAL pipeline against REAL MySQL (NSG_TEST_MYSQL, *_test only): real JWT validation of both schemes, real authorization
// policies, middleware, multipart binding, controller, services, repositories (SQL), envelope filter and real files in a temporary directory.
// Only the two session look-ups are in-memory (they answer "is this session live"), so tokens can be minted without a login flow; the tokens
// themselves are produced by the real token services and validated by the real handlers.

[Collection("mysql")]
public sealed class Form16AuthorizationHostTests : IAsyncLifetime, IDisposable
{
    private const long SalaryAdminA = 9001, SalaryAdminB = 9002;
    private const string Year = "2026-27";

    // Two employees with documents, one with none, one whose profile is inactive.
    private const string AliceNsg = "F16-1001", BobNsg = "F16-1002", DaveNsg = "F16-1003", OddNsg = "F16/1.004";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-form16-host-" + Guid.NewGuid().ToString("N"));
    private readonly ApiFactory _baseFactory;
    private readonly FakeAdminSessions _adminSessions = new();
    private readonly FakeMobileSessions _mobileSessions = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Dictionary<string, long> _profileIds = new();

    public Form16AuthorizationHostTests(DatabaseFixture fixture)
    {
        Directory.CreateDirectory(_root);   // Storage:RootPath must exist before the host starts
        _baseFactory = fixture.Factory;
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<StorageOptions>(o => o.RootPath = _root);
            services.RemoveAll<IAdminRefreshTokenRepository>();
            services.AddSingleton<IAdminRefreshTokenRepository>(_adminSessions);
            services.RemoveAll<IMobileRefreshTokenRepository>();
            services.AddSingleton<IMobileRefreshTokenRepository>(_mobileSessions);
        }));
    }

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await SeedPersonAsync(AliceNsg, "Alice Kumar", "9822200001");
        await SeedPersonAsync(BobNsg, "Bob Singh", "9822200002");
        await SeedPersonAsync(DaveNsg, "Dave Rao", "9822200003");
        await SeedPersonAsync(OddNsg, "Odd Number", "9822200004");
        _profileIds["inactive"] = await SeedProfileAsync(AliceNsg + "-X", "9822200005", active: false);
    }

    public async Task DisposeAsync() => await CleanAsync();

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _baseFactory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `personnel_form16`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles` WHERE `nsg_number` LIKE 'F16%'");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
    }

    private async Task SeedPersonAsync(string nsg, string name, string mobile)
    {
        await using AppDbContext ctx = _baseFactory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = name, MobileNumber = mobile, DateOfBirth = new DateOnly(1985, 6, 15), IsActive = true });
        await ctx.SaveChangesAsync();
        _profileIds[nsg] = await SeedProfileAsync(nsg, mobile, active: true);
    }

    private async Task<long> SeedProfileAsync(string nsg, string mobile, bool active)
    {
        await using AppDbContext ctx = _baseFactory.CreateContext();
        MobileUserProfile profile = new() { NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = new DateOnly(1985, 6, 15), Role = UserRole.Employee, IsActive = active };
        ctx.Set<MobileUserProfile>().Add(profile);
        await ctx.SaveChangesAsync();
        return profile.Id;
    }

    // ── Tokens (real token services; the session is registered as live) ──

    private string AdminToken(string roleCode, long adminId = SalaryAdminA)
    {
        Guid sid = Guid.NewGuid();
        _adminSessions.Active.Add((sid, adminId, roleCode));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAdminTokenService>()
            .GenerateAdminAccessToken(new AdminProfile { Id = adminId }, roleCode, sid).Token;
    }

    private string SalaryToken(long adminId = SalaryAdminA) => AdminToken(AdminRoleCodes.SalaryManagementAdmin, adminId);

    private string MobileToken(string nsg)
    {
        long profileId = _profileIds[nsg];
        Guid sid = Guid.NewGuid();
        _mobileSessions.Active.Add((sid, profileId));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateMobileAccessToken(new MobileUserProfile { Id = profileId }, sid).Token;
    }

    // ── Requests ──

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer, HttpContent? content = null)
    {
        HttpRequestMessage request = new(method, url) { Content = content };
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _factory.CreateClient().SendAsync(request);
    }

    private static byte[] PdfBytes(string marker) => Encoding.ASCII.GetBytes("%PDF-1.4\n" + marker + "\n%%EOF");

    private static MultipartFormDataContent Upload(string? nsg, string? year, string? partA = null, string? partB = null, bool notPdf = false)
    {
        MultipartFormDataContent form = new();
        if (nsg != null) form.Add(new StringContent(nsg), "nsgNumber");
        if (year != null) form.Add(new StringContent(year), "assessmentYear");
        foreach ((string field, string? marker) in new[] { ("partA", partA), ("partB", partB) })
        {
            if (marker == null) continue;
            ByteArrayContent file = new(notPdf ? Encoding.ASCII.GetBytes("this is not a pdf") : PdfBytes(marker));
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            form.Add(file, field, field + ".pdf");
        }

        return form;
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<JsonElement> DataAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        JsonElement root = (await BodyAsync(response)).RootElement.Clone();
        Assert.Equal(new[] { "success", "message", "data" }, root.EnumerateObject().Select(p => p.Name));
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        return root.GetProperty("data");
    }

    private async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode expected, string? messagePart = null)
    {
        Assert.Equal(expected, response.StatusCode);
        using JsonDocument doc = await BodyAsync(response);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        string message = doc.RootElement.GetProperty("message").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(message));
        if (messagePart != null) Assert.Contains(messagePart, message);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").ValueKind);
        Assert.DoesNotContain(_root, message);
    }

    private async Task<long> UploadAsync(string nsg, string year, string? a, string? b, long admin = SalaryAdminA)
    {
        JsonElement data = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", SalaryToken(admin), Upload(nsg, year, a, b)), HttpStatusCode.Created);
        return data.GetProperty("id").GetInt64();
    }

    private static readonly (HttpMethod Method, string Url, bool Multipart)[] AdminRoutes =
    {
        (HttpMethod.Post, "/api/v1/admin/form16", true),
        (HttpMethod.Put, "/api/v1/admin/form16", true),
        (HttpMethod.Get, "/api/v1/admin/form16", false),
        (HttpMethod.Get, "/api/v1/admin/form16/1", false)
    };

    private static readonly string[] MobileRoutes =
    {
        "/api/v1/mobile/form16", $"/api/v1/mobile/form16/{Year}", $"/api/v1/mobile/form16/{Year}/part-a", $"/api/v1/mobile/form16/{Year}/part-b"
    };

    private static HttpContent? BodyFor((HttpMethod Method, string Url, bool Multipart) route) => route.Multipart ? Upload(AliceNsg, Year, "A") : null;

    // ── Who may call the admin APIs ──

    [Fact]
    public async Task Every_admin_route_rejects_an_anonymous_caller_with_401_and_stores_nothing()
    {
        foreach (var route in AdminRoutes)
            await AssertErrorAsync(await SendAsync(route.Method, route.Url, null, BodyFor(route)), HttpStatusCode.Unauthorized);

        await using AppDbContext ctx = _baseFactory.CreateContext();
        Assert.Empty(await ctx.PersonnelForm16.ToListAsync());
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));   // nothing was written
    }

    [Fact]
    public async Task A_tampered_or_garbage_token_is_401_on_every_route()
    {
        string good = SalaryToken();
        string tampered = good[..^3] + (good[^3] == 'A' ? "B" : "A") + good[^2..];

        foreach (string bad in new[] { tampered, "not-a-jwt", good + "x" })
            foreach (var route in AdminRoutes)
                Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(route.Method, route.Url, bad, BodyFor(route))).StatusCode);
        foreach (string url in MobileRoutes)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, url, tampered)).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_whose_session_has_ended_is_401()
    {
        string token = SalaryToken();
        _adminSessions.Active.Clear();   // logout / revoked session

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/admin/form16", token)).StatusCode);
    }

    [Fact]
    public async Task A_mobile_token_is_refused_on_every_admin_route_and_stores_nothing()
    {
        string mobile = MobileToken(AliceNsg);

        foreach (var route in AdminRoutes)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(route.Method, route.Url, mobile, BodyFor(route))).StatusCode);

        await using AppDbContext ctx = _baseFactory.CreateContext();
        Assert.Empty(await ctx.PersonnelForm16.ToListAsync());
    }

    [Theory]
    [InlineData(AdminRoleCodes.SuperAdmin)]
    [InlineData(AdminRoleCodes.RegistrationAdmin)]
    [InlineData(AdminRoleCodes.PisAdmin)]
    [InlineData(AdminRoleCodes.GrievanceAdmin)]
    [InlineData(AdminRoleCodes.MediaCellAdmin)]
    [InlineData(AdminRoleCodes.LeaveAdmin)]
    public async Task Every_other_admin_role_gets_403_on_every_admin_route_and_stores_nothing(string role)
    {
        string token = AdminToken(role);

        foreach (var route in AdminRoutes)
            await AssertErrorAsync(await SendAsync(route.Method, route.Url, token, BodyFor(route)), HttpStatusCode.Forbidden);

        await using AppDbContext ctx = _baseFactory.CreateContext();
        Assert.Empty(await ctx.PersonnelForm16.ToListAsync());
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));   // nothing was written
    }

    [Fact]
    public async Task An_admin_token_is_refused_on_every_mobile_route()
    {
        await UploadAsync(AliceNsg, Year, "A", "B");

        foreach (string role in new[] { AdminRoleCodes.SalaryManagementAdmin, AdminRoleCodes.SuperAdmin })
            foreach (string url in MobileRoutes)
                Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, url, AdminToken(role))).StatusCode);
    }

    [Fact]
    public async Task Every_mobile_route_rejects_an_anonymous_caller_with_401()
    {
        await UploadAsync(AliceNsg, Year, "A", "B");

        foreach (string url in MobileRoutes)
            await AssertErrorAsync(await SendAsync(HttpMethod.Get, url, null), HttpStatusCode.Unauthorized);
    }

    // ── The Salary Admin: all four admin APIs, over all employees ──

    [Fact]
    public async Task The_salary_admin_can_use_all_four_admin_apis_over_real_mysql()
    {
        string admin = SalaryToken();

        // POST (create, Part A only), then PUT adds Part B, then both reads.
        JsonElement created = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, partA: "ALICE-A")), HttpStatusCode.Created);
        long id = created.GetProperty("id").GetInt64();
        Assert.Equal(("Alice Kumar", Year, true, false), (created.GetProperty("employeeName").GetString(), created.GetProperty("assessmentYear").GetString(),
            created.GetProperty("partAAvailable").GetBoolean(), created.GetProperty("partBAvailable").GetBoolean()));
        Assert.EndsWith("+05:30", created.GetProperty("partAUploadedAt").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("partBUploadedAt").ValueKind);

        JsonElement updated = await DataAsync(await SendAsync(HttpMethod.Put, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, partB: "ALICE-B")), HttpStatusCode.OK);
        Assert.Equal(id, updated.GetProperty("id").GetInt64());
        Assert.True(updated.GetProperty("partAAvailable").GetBoolean() && updated.GetProperty("partBAvailable").GetBoolean());

        JsonElement byId = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/form16/{id}", admin), HttpStatusCode.OK);
        Assert.Equal(AliceNsg, byId.GetProperty("nsgNumber").GetString());
        Assert.True(byId.GetProperty("partBAvailable").GetBoolean());

        JsonElement list = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/form16?nsgNumber={AliceNsg}&assessmentYear={Year}", admin), HttpStatusCode.OK);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetInt64());

        // The files are on disk under the documented layout; no path appears in any response body.
        Assert.True(File.Exists(Path.Combine(_root, "Form16", Year, AliceNsg, "part-a.pdf")));
        Assert.True(File.Exists(Path.Combine(_root, "Form16", Year, AliceNsg, "part-b.pdf")));
        Assert.DoesNotContain(".pdf", byId.GetRawText());
    }

    [Fact]
    public async Task Any_salary_admin_sees_and_manages_every_employees_records_whoever_uploaded_them()
    {
        long aliceId = await UploadAsync(AliceNsg, Year, "ALICE-A", null, admin: SalaryAdminA);
        long bobId = await UploadAsync(BobNsg, Year, null, "BOB-B", admin: SalaryAdminB);

        // Admin B reads A's record and A reads B's; the list holds both.
        string adminB = SalaryToken(SalaryAdminB), adminA = SalaryToken(SalaryAdminA);
        Assert.Equal(AliceNsg, (await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/form16/{aliceId}", adminB), HttpStatusCode.OK)).GetProperty("nsgNumber").GetString());
        Assert.Equal(BobNsg, (await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/form16/{bobId}", adminA), HttpStatusCode.OK)).GetProperty("nsgNumber").GetString());
        JsonElement list = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/form16", adminA), HttpStatusCode.OK);
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());

        // Admin B updates Alice's record (uploaded by A): allowed, and uploaded_by follows the token.
        await DataAsync(await SendAsync(HttpMethod.Put, "/api/v1/admin/form16", adminB, Upload(AliceNsg, Year, partB: "ALICE-B")), HttpStatusCode.OK);
        await using AppDbContext ctx = _baseFactory.CreateContext();
        PersonnelForm16 alice = await ctx.PersonnelForm16.AsNoTracking().SingleAsync(f => f.Id == aliceId);
        Assert.Equal(SalaryAdminB, alice.UploadedBy);
        Assert.NotNull(alice.PartAFilePath);   // A's Part A preserved
        Assert.NotNull(alice.PartBFilePath);
    }

    [Fact]
    public async Task The_admin_apis_report_errors_with_the_envelope_and_a_message_and_change_nothing()
    {
        string admin = SalaryToken();

        await AssertErrorAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year)), HttpStatusCode.BadRequest, "Upload at least one PDF");
        await AssertErrorAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload("F16-NOPE", Year, "A")), HttpStatusCode.NotFound, "No personnel record");
        await AssertErrorAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, "2026", "A")), HttpStatusCode.BadRequest, "assessmentYear");
        await AssertErrorAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(null, Year, "A")), HttpStatusCode.BadRequest, "nsgNumber");
        await AssertErrorAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, "A", notPdf: true)), HttpStatusCode.BadRequest, "not a PDF");
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, "A")), HttpStatusCode.NotFound, "not found");
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/form16/999999", admin), HttpStatusCode.NotFound, "not found");
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/form16?assessmentYear=bad", admin), HttpStatusCode.BadRequest, "assessmentYear");
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/form16?pageSize=101", admin), HttpStatusCode.BadRequest, "pageSize");

        await using AppDbContext ctx = _baseFactory.CreateContext();
        Assert.Empty(await ctx.PersonnelForm16.ToListAsync());
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : Array.Empty<string>());

        // An empty list is a 200 with a message (and no items), not a blank body.
        using JsonDocument empty = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/form16", admin));
        Assert.Equal("No Form 16 records found.", empty.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_second_post_for_the_same_employee_and_year_updates_and_never_duplicates_the_row()
    {
        string admin = SalaryToken();
        await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, "A1", "B1")), HttpStatusCode.Created);
        await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg.ToLowerInvariant(), Year, "A2")), HttpStatusCode.OK);

        await using AppDbContext ctx = _baseFactory.CreateContext();
        PersonnelForm16 row = await ctx.PersonnelForm16.AsNoTracking().SingleAsync();
        Assert.Equal(AliceNsg, row.NsgNumber);   // PIS spelling, not the typed lower case
        Assert.Contains("A2", await File.ReadAllTextAsync(Path.Combine(_root, "Form16", Year, AliceNsg, "part-a.pdf")));
        Assert.Contains("B1", await File.ReadAllTextAsync(Path.Combine(_root, "Form16", Year, AliceNsg, "part-b.pdf")));
    }

    [Fact]
    public async Task Concurrent_posts_for_the_same_employee_and_year_leave_one_row_and_consistent_files()
    {
        string admin = SalaryToken();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            SendAsync(HttpMethod.Post, "/api/v1/admin/form16", admin, Upload(AliceNsg, Year, partA: "A-" + i, partB: "B-" + i))));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        await using AppDbContext ctx = _baseFactory.CreateContext();
        Assert.Single(await ctx.PersonnelForm16.AsNoTracking().ToListAsync());
        Assert.Equal(2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);   // no .bak or staging leftovers
    }

    // ── Mobile users: only their own documents ──

    [Fact]
    public async Task A_mobile_user_sees_and_downloads_only_their_own_documents()
    {
        await UploadAsync(AliceNsg, Year, "ALICE-A", "ALICE-B");
        await UploadAsync(AliceNsg, "2025-26", null, "ALICE-OLD-B");
        await UploadAsync(BobNsg, Year, "BOB-A", "BOB-B");
        await UploadAsync(BobNsg, "2024-25", "BOB-OLD-A", null);
        string alice = MobileToken(AliceNsg), bob = MobileToken(BobNsg);

        // Lists
        JsonElement aliceList = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16", alice), HttpStatusCode.OK);
        Assert.Equal(new[] { Year, "2025-26" }, aliceList.EnumerateArray().Select(e => e.GetProperty("assessmentYear").GetString()));
        JsonElement bobList = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16", bob), HttpStatusCode.OK);
        Assert.Equal(new[] { Year, "2024-25" }, bobList.EnumerateArray().Select(e => e.GetProperty("assessmentYear").GetString()));
        string aliceJson = aliceList.GetRawText();
        Assert.DoesNotContain(".pdf", aliceJson);
        Assert.DoesNotContain(_root.Replace("\\", "\\\\"), aliceJson);
        Assert.DoesNotContain(BobNsg, aliceJson);

        // Details
        JsonElement details = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}", alice), HttpStatusCode.OK);
        Assert.True(details.GetProperty("partAAvailable").GetBoolean() && details.GetProperty("partBAvailable").GetBoolean());
        Assert.EndsWith("+05:30", details.GetProperty("partAUploadedAt").GetString());
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16/2024-25", alice), HttpStatusCode.NotFound);   // Bob's year only

        // Streams
        HttpResponseMessage partA = await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-a", alice);
        Assert.Equal(HttpStatusCode.OK, partA.StatusCode);
        Assert.Equal("application/pdf", partA.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PdfBytes("ALICE-A"), await partA.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", partA.Headers.GetValues("Cache-Control").Single());
        Assert.Equal("nosniff", partA.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("Form16_2026-27_PartA.pdf", partA.Content.Headers.ContentDisposition!.FileNameStar ?? partA.Content.Headers.ContentDisposition.FileName);
        Assert.Equal(PdfBytes("BOB-B"), await (await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-b", bob)).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task One_employee_can_never_reach_another_employees_document_by_any_route()
    {
        await UploadAsync(BobNsg, Year, "BOB-SECRET-A", "BOB-SECRET-B");
        await UploadAsync(DaveNsg, "2025-26", "DAVE-A", null);
        string alice = MobileToken(AliceNsg);   // Alice has no documents at all

        JsonElement none = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16", alice), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Array, none.ValueKind);
        Assert.Equal(0, none.GetArrayLength());
        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16", alice)))
            Assert.Equal("No Form 16 documents found.", doc.RootElement.GetProperty("message").GetString());

        foreach (string url in new[] { $"/api/v1/mobile/form16/{Year}", $"/api/v1/mobile/form16/{Year}/part-a", $"/api/v1/mobile/form16/{Year}/part-b", "/api/v1/mobile/form16/2025-26/part-a" })
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, url, alice);
            await AssertErrorAsync(response, HttpStatusCode.NotFound);
            Assert.DoesNotContain("BOB-SECRET", await response.Content.ReadAsStringAsync());
        }

        // Smuggling an NSG number or record id in the query string, header or route changes nothing: the token decides.
        foreach (string url in new[] { $"/api/v1/mobile/form16/{Year}/part-a?nsgNumber={BobNsg}", $"/api/v1/mobile/form16/{Year}/part-a?id=1", $"/api/v1/mobile/form16?nsgNumber={BobNsg}" })
        {
            HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alice);
            request.Headers.Add("X-Nsg-Number", BobNsg);
            HttpResponseMessage response = await _factory.CreateClient().SendAsync(request);
            Assert.DoesNotContain("BOB-SECRET", await response.Content.ReadAsStringAsync());
            Assert.NotEqual(HttpStatusCode.OK, url.Contains("part-a") ? response.StatusCode : HttpStatusCode.NotFound);
        }

        // Path-traversal-looking years are rejected as a bad year, never resolved against the disk.
        foreach (string year in new[] { "..", "2026-27%2F..%2F..", "%2e%2e", "..%5c..", "2026" })
            Assert.Contains((await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{year}/part-a", alice)).StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task An_employee_whose_nsg_number_has_unusual_characters_gets_exactly_their_document()
    {
        await UploadAsync(OddNsg, Year, "ODD-A", null);
        await UploadAsync(BobNsg, Year, "BOB-A", null);

        HttpResponseMessage odd = await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-a", MobileToken(OddNsg));
        Assert.Equal(PdfBytes("ODD-A"), await odd.Content.ReadAsByteArrayAsync());
        Assert.Equal(PdfBytes("BOB-A"), await (await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-a", MobileToken(BobNsg))).Content.ReadAsByteArrayAsync());

        string[] folders = Directory.GetDirectories(Path.Combine(_root, "Form16", Year)).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;
        Assert.Equal(new[] { BobNsg, "F16%2F1%2E004" }, folders);   // the odd number is one encoded folder, inside the year folder
    }

    [Fact]
    public async Task Missing_documents_are_404_with_a_message_and_a_missing_file_does_not_reveal_the_path()
    {
        await UploadAsync(AliceNsg, Year, "ALICE-A", null);
        string alice = MobileToken(AliceNsg);

        await AssertErrorAsync(await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-b", alice), HttpStatusCode.NotFound, "Part B is not available");
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16/2020-21", alice), HttpStatusCode.NotFound);
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, "/api/v1/mobile/form16/2026-30", alice), HttpStatusCode.BadRequest, "assessmentYear");

        File.Delete(Path.Combine(_root, "Form16", Year, AliceNsg, "part-a.pdf"));   // database says yes, disk says no
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, $"/api/v1/mobile/form16/{Year}/part-a", alice), HttpStatusCode.NotFound, "not available");
    }

    [Fact]
    public async Task A_deactivated_profile_or_an_unknown_profile_cannot_read_anything()
    {
        await UploadAsync(AliceNsg, Year, "ALICE-A", null);
        Guid sid = Guid.NewGuid();
        _mobileSessions.Active.Add((sid, _profileIds["inactive"]));
        string inactive;
        using (IServiceScope scope = _factory.Services.CreateScope())
            inactive = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateMobileAccessToken(new MobileUserProfile { Id = _profileIds["inactive"] }, sid).Token;

        Guid ghostSid = Guid.NewGuid();
        _mobileSessions.Active.Add((ghostSid, 99999999));
        string ghost;
        using (IServiceScope scope = _factory.Services.CreateScope())
            ghost = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateMobileAccessToken(new MobileUserProfile { Id = 99999999 }, ghostSid).Token;

        foreach (string token in new[] { inactive, ghost })
            foreach (string url in MobileRoutes)
                Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, url, token)).StatusCode);
    }

    // ── In-memory session look-ups ──

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

        public Task<SessionStartOutcome> StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
