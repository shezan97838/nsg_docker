using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// The Leave module through the REAL pipeline: real JWT validation (both schemes), real policies, real middleware, controllers, services, the
/// envelope filter and the Excel reader. Only the outermost edges are in-memory fakes: the leave repository, audit and the two session look-ups.
/// A dummy connection string and disabled migrations guarantee nothing can reach MySQL.
///
/// NOT covered: the real SQL of the repository (see LeaveDatabaseTests, which needs NSG_TEST_MYSQL) and the real admin session look-up.
/// </summary>
[Collection("GrievanceHost")]
public sealed class LeaveAuthorizationHostTests : IDisposable
{
    private static readonly string[] Variables =
    {
        "ConnectionStrings__MySql", "Database__MigrateOnStartup", "Security__DisableAuthorization", "Jwt__Secret",
        "Encryption__Key", "BlindIndex__Key", "ASPNETCORE_ENVIRONMENT"
    };

    private const long AdminId = 9001, EmployeeId = 501, OtherEmployeeId = 502;

    private readonly Dictionary<string, string?> _previous = new();
    private readonly LeaveFakeRepository _repo = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeAdminSessions _adminSessions = new();
    private readonly FakeMobileSessions _mobileSessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public LeaveAuthorizationHostTests()
    {
        foreach (string name in Variables) _previous[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=127.0.0.1;Port=1;Database=host_check_dummy;Uid=x;Pwd=x;");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        Environment.SetEnvironmentVariable("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("BlindIndex__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        _repo.AddPerson("NSG1001");
        _repo.AddPerson("NSG1002");
        _repo.MobileProfiles[EmployeeId] = "NSG1001";
        _repo.MobileProfiles[OtherEmployeeId] = "NSG1002";

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILeaveRepository>();
            services.AddSingleton<ILeaveRepository>(_repo);
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

    private string AdminToken(string roleCode, long profileId = AdminId)
    {
        Guid sid = Guid.NewGuid();
        _adminSessions.Active.Add((sid, profileId, roleCode));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAdminTokenService>()
            .GenerateAdminAccessToken(new AdminProfile { Id = profileId }, roleCode, sid).Token;
    }

    private string MobileToken(long profileId = EmployeeId)
    {
        Guid sid = Guid.NewGuid();
        _mobileSessions.Active.Add((sid, profileId));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateMobileAccessToken(new MobileUserProfile { Id = profileId }, sid).Token;
    }

    // ── Requests ──

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private static MultipartFormDataContent FileForm(byte[] bytes, string name = "leave.xlsx", string field = "file")
    {
        MultipartFormDataContent form = new();
        ByteArrayContent part = new(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(part, field, name);
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

    /// <summary>Every Leave Admin route, each ready to send (new content per call).</summary>
    private static IEnumerable<(string Name, HttpMethod Method, string Url, Func<HttpContent?> Content)> AdminRoutes() => new (string, HttpMethod, string, Func<HttpContent?>)[]
    {
        ("create", HttpMethod.Post, "/api/v1/admin/leave-records", () => Json("{\"nsgNumber\":\"NSG1001\",\"leaveTaken\":5}")),
        ("update", HttpMethod.Put, "/api/v1/admin/leave-records/NSG1001", () => Json("{\"leaveTaken\":6}")),
        ("get", HttpMethod.Get, "/api/v1/admin/leave-records/NSG1001", () => null),
        ("history", HttpMethod.Get, "/api/v1/admin/leave-records/NSG1001/history", () => null),
        ("upload", HttpMethod.Post, "/api/v1/admin/leave-uploads", () => FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5) }))),
        ("batches", HttpMethod.Get, "/api/v1/admin/leave-uploads", () => null),
        ("batch", HttpMethod.Get, "/api/v1/admin/leave-uploads/1", () => null),
        ("batch-errors", HttpMethod.Get, "/api/v1/admin/leave-uploads/1/errors", () => null)
    };

    private async Task AssertEveryAdminRouteAsync(string? bearer, HttpStatusCode expected)
    {
        foreach (var route in AdminRoutes())
        {
            HttpResponseMessage response = await SendAsync(route.Method, route.Url, bearer, route.Content());
            Assert.True(response.StatusCode == expected, $"{route.Method} {route.Url} -> {(int)response.StatusCode}, expected {(int)expected}");
        }
    }

    private void AssertNothingChanged()
    {
        Assert.Empty(_repo.Records);
        Assert.Empty(_repo.History);
        Assert.Empty(_repo.Batches);
        Assert.Empty(_audit.Entries);
    }

    // ═══════════════ Authorization ═══════════════

    [Fact]
    public async Task Without_a_token_every_leave_admin_route_is_401_and_nothing_happens()
    {
        await AssertEveryAdminRouteAsync(null, HttpStatusCode.Unauthorized);

        AssertNothingChanged();
    }

    [Theory]
    [InlineData(AdminRoleCodes.SuperAdmin)]
    [InlineData(AdminRoleCodes.RegistrationAdmin)]
    [InlineData(AdminRoleCodes.PisAdmin)]
    [InlineData(AdminRoleCodes.GrievanceAdmin)]
    [InlineData(AdminRoleCodes.MediaCellAdmin)]
    [InlineData(AdminRoleCodes.SalaryManagementAdmin)]
    public async Task Every_other_admin_role_including_super_admin_is_403_on_every_leave_admin_route(string role)
    {
        await AssertEveryAdminRouteAsync(AdminToken(role), HttpStatusCode.Forbidden);

        AssertNothingChanged();
    }

    [Fact]
    public async Task A_mobile_token_is_401_on_every_leave_admin_route_and_cannot_write_leave()
    {
        await AssertEveryAdminRouteAsync(MobileToken(), HttpStatusCode.Unauthorized);

        AssertNothingChanged();
    }

    [Fact]
    public async Task A_leave_admin_can_use_every_leave_admin_route()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        HttpResponseMessage created = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-records", token, Json("{\"nsgNumber\":\"NSG1001\",\"leaveTaken\":5}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.StartsWith("/api/v1/admin/leave-records/NSG1001", created.Headers.Location!.AbsolutePath);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/v1/admin/leave-records/NSG1001", token, Json("{\"leaveTaken\":6}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/leave-records/NSG1001", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/leave-records/NSG1001/history", token)).StatusCode);
        HttpResponseMessage upload = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", token, FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1002", 3) })));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/admin/leave-uploads", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/admin/leave-uploads/{_repo.Batches.Single().Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/admin/leave-uploads/{_repo.Batches.Single().Id}/errors", token)).StatusCode);
    }

    [Fact]
    public async Task The_leave_admin_role_opens_no_other_module()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        foreach (string url in new[] { "/api/v1/admin/grievances", "/api/v1/admin/dashboard/summary", "/api/v1/admin/grievance-categories", "/api/v1/admin/accounts" })
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, url, token)).StatusCode);
    }

    // ═══════════════ Manual create / update over HTTP ═══════════════

    [Fact]
    public async Task Create_then_update_returns_the_envelope_with_201_then_200_and_takes_the_actor_from_the_token_not_the_body()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        // The body tries to smuggle in a year and an actor; the DTO has no such fields and the server uses the token and the clock.
        HttpResponseMessage created = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-records", token,
            Json("{\"nsgNumber\":\"NSG1001\",\"leaveTaken\":30,\"leaveYear\":1999,\"createdBy\":7,\"updatedBy\":7}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (JsonDocument body = await BodyAsync(created))
        {
            Assert.Equal(new[] { "success", "message", "data" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.True(body.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Leave record created successfully.", body.RootElement.GetProperty("message").GetString());
            JsonElement data = body.RootElement.GetProperty("data");
            Assert.Equal(("NSG1001", 30), (data.GetProperty("nsgNumber").GetString(), data.GetProperty("leaveTaken").GetInt32()));
            Assert.NotEqual(1999, data.GetProperty("leaveYear").GetInt32());
            Assert.EndsWith("+05:30", data.GetProperty("createdAt").GetString());
            Assert.Equal(AdminId, data.GetProperty("createdBy").GetInt64());
        }

        PersonnelLeaveRecord record = _repo.Records.Single();
        Assert.Equal((AdminId, AdminId), (record.CreatedBy, record.UpdatedBy));
        Assert.NotEqual(1999, record.LeaveYear);

        HttpResponseMessage updated = await SendAsync(HttpMethod.Put, "/api/v1/admin/leave-records/NSG1001", AdminToken(AdminRoleCodes.LeaveAdmin, profileId: 9002), Json("{\"leaveTaken\":35}"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal((35, 9002L, AdminId), (record.LeaveTaken, record.UpdatedBy, record.CreatedBy));
        Assert.Equal(2, _repo.History.Count);
    }

    [Fact]
    public async Task Bad_manual_requests_get_the_error_envelope_and_the_right_status()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        async Task<(HttpStatusCode, string)> Post(string json)
        {
            HttpResponseMessage r = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-records", token, Json(json));
            using JsonDocument b = await BodyAsync(r);
            Assert.False(b.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, b.RootElement.GetProperty("data").ValueKind);
            return (r.StatusCode, b.RootElement.GetProperty("message").GetString()!);
        }

        (HttpStatusCode negative, string negativeMessage) = await Post("{\"nsgNumber\":\"NSG1001\",\"leaveTaken\":-1}");
        Assert.Equal(HttpStatusCode.BadRequest, negative);
        Assert.Contains("must not be negative", negativeMessage);

        (HttpStatusCode unknown, string unknownMessage) = await Post("{\"nsgNumber\":\"NSG9999\",\"leaveTaken\":1}");
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal("No active personnel record exists for this NSG number.", unknownMessage);

        (HttpStatusCode missing, _) = await Post("{\"nsgNumber\":\"NSG1001\"}");
        Assert.Equal(HttpStatusCode.BadRequest, missing);

        HttpResponseMessage noRecord = await SendAsync(HttpMethod.Put, "/api/v1/admin/leave-records/NSG1001", token, Json("{\"leaveTaken\":3}"));
        Assert.Equal(HttpStatusCode.NotFound, noRecord.StatusCode);

        Assert.Empty(_repo.Records);
    }

    [Fact]
    public async Task The_history_endpoint_answers_with_a_message_when_there_is_no_history()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/admin/leave-records/NSG1001/history", AdminToken(AdminRoleCodes.LeaveAdmin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.Equal("No leave changes found for this NSG number.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    // ═══════════════ Bulk upload over HTTP ═══════════════

    [Fact]
    public async Task A_valid_workbook_uploaded_over_http_creates_the_records_and_returns_the_summary_in_the_envelope()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", token,
            FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5), ("NSG1002", 6) })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("2 created, 0 updated, 0 unchanged", body.RootElement.GetProperty("message").GetString());
        JsonElement data = body.RootElement.GetProperty("data");
        Assert.Equal(("Completed", 2, 2, 0, 0), (data.GetProperty("uploadStatus").GetString(), data.GetProperty("totalRows").GetInt32(),
            data.GetProperty("createdCount").GetInt32(), data.GetProperty("updatedCount").GetInt32(), data.GetProperty("failedCount").GetInt32()));
        Assert.True(data.GetProperty("id").GetInt64() > 0);
        Assert.Equal(2, _repo.Records.Count);
        Assert.Equal(AdminId, _repo.Batches.Single().CreatedBy);
    }

    [Fact]
    public async Task An_invalid_workbook_is_400_with_the_envelope_changes_nothing_and_leaves_a_readable_report()
    {
        string token = AdminToken(AdminRoleCodes.LeaveAdmin);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", token,
            FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5), ("NSG9999", 6), ("NSG1002", -1) })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (JsonDocument body = await BodyAsync(response))
        {
            Assert.False(body.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
            Assert.Contains("2 of 3 rows have errors", body.RootElement.GetProperty("message").GetString());
        }

        Assert.Empty(_repo.Records);
        long batchId = _repo.Batches.Single().Id;

        HttpResponseMessage errors = await SendAsync(HttpMethod.Get, $"/api/v1/admin/leave-uploads/{batchId}/errors", token);
        Assert.Equal(HttpStatusCode.OK, errors.StatusCode);
        using JsonDocument report = await BodyAsync(errors);
        Assert.Equal(new[] { 3, 4 }, report.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(e => e.GetProperty("rowNumber").GetInt32()).ToArray());

        HttpResponseMessage batch = await SendAsync(HttpMethod.Get, $"/api/v1/admin/leave-uploads/{batchId}", token);
        using JsonDocument status = await BodyAsync(batch);
        Assert.Equal("ValidationFailed", status.RootElement.GetProperty("data").GetProperty("uploadStatus").GetString());
    }

    [Fact]
    public async Task A_request_without_the_file_field_is_a_400_with_a_message()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", AdminToken(AdminRoleCodes.LeaveAdmin),
            FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5) }), field: "workbook"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.Equal("An Excel file (.xlsx) is required.", body.RootElement.GetProperty("message").GetString());
        Assert.Empty(_repo.Batches);
    }

    [Fact]
    public async Task An_upload_far_over_the_size_limit_is_a_400_with_a_message_not_a_500_and_changes_no_leave()
    {
        // 17 MB: above the 16 MB request ceiling (rejected while the form is bound, if the test server enforces it) and in any case above the
        // 2 MB file limit (rejected by the service). Either way: 400 with a message, and no leave data touched.
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", AdminToken(AdminRoleCodes.LeaveAdmin),
            FileForm(new byte[17 * 1024 * 1024]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        string message = body.RootElement.GetProperty("message").GetString()!;
        Assert.True(message.Contains("too large") || message.Contains("larger than"), message);
        Assert.Empty(_repo.Records);
        Assert.Empty(_repo.History);
    }

    [Fact]
    public async Task A_second_upload_while_one_is_running_is_a_409_with_a_message()
    {
        await _repo.TryStartBatchAsync("running.xlsx", 2026, 9002, DateTime.UtcNow.AddHours(5.5), TimeSpan.FromHours(1));

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", AdminToken(AdminRoleCodes.LeaveAdmin),
            FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5) })));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.Contains("already being processed", body.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(BulkFault.DatabaseErrorAfterWrites)]
    [InlineData(BulkFault.OutcomeUnknown)]
    public async Task A_failed_or_uncertain_transaction_is_never_a_success_response(BulkFault fault)
    {
        _repo.Fault = fault;

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/leave-uploads", AdminToken(AdminRoleCodes.LeaveAdmin),
            FileForm(XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5) })));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(_repo.Records);
        Assert.DoesNotContain("Exception", await response.Content.ReadAsStringAsync());
    }

    // ═══════════════ Mobile: own record, read only ═══════════════

    [Fact]
    public async Task A_mobile_user_gets_only_their_own_current_year_record_in_the_envelope()
    {
        int year = NSG.Mobile.Domain.Common.IndianStandardTime.Now.Year;
        _repo.AddRecord("NSG1001", year, 11);
        _repo.AddRecord("NSG1002", year, 99);

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/leave/my-record", MobileToken(EmployeeId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        JsonElement data = body.RootElement.GetProperty("data");
        Assert.Equal(new[] { "nsgNumber", "leaveYear", "leaveTaken", "lastUpdatedAt" }, data.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(("NSG1001", 11), (data.GetProperty("nsgNumber").GetString(), data.GetProperty("leaveTaken").GetInt32()));
        Assert.EndsWith("+05:30", data.GetProperty("lastUpdatedAt").GetString());
        Assert.DoesNotContain("NSG1002", await response.Content.ReadAsStringAsync());   // the other person never appears
    }

    [Fact]
    public async Task Another_nsg_number_in_the_query_or_headers_cannot_change_whose_record_is_returned()
    {
        int year = NSG.Mobile.Domain.Common.IndianStandardTime.Now.Year;
        _repo.AddRecord("NSG1001", year, 11);
        _repo.AddRecord("NSG1002", year, 99);
        HttpClient client = _factory.CreateClient();

        foreach (string url in new[] { "/api/v1/leave/my-record?nsgNumber=NSG1002", "/api/v1/leave/my-record?nsg=NSG1002&profileId=502&userId=502" })
        {
            HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MobileToken(EmployeeId));
            request.Headers.Add("X-Nsg-Number", "NSG1002");
            HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = await BodyAsync(response);
            Assert.Equal("NSG1001", body.RootElement.GetProperty("data").GetProperty("nsgNumber").GetString());
        }
    }

    [Fact]
    public async Task The_other_user_sees_their_own_record_with_their_own_token()
    {
        int year = NSG.Mobile.Domain.Common.IndianStandardTime.Now.Year;
        _repo.AddRecord("NSG1001", year, 11);
        _repo.AddRecord("NSG1002", year, 99);

        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/leave/my-record", MobileToken(OtherEmployeeId));

        using JsonDocument body = await BodyAsync(response);
        Assert.Equal(("NSG1002", 99), (body.RootElement.GetProperty("data").GetProperty("nsgNumber").GetString(), body.RootElement.GetProperty("data").GetProperty("leaveTaken").GetInt32()));
    }

    [Fact]
    public async Task No_current_year_record_is_a_404_with_a_message_and_the_get_creates_nothing()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/leave/my-record", MobileToken(EmployeeId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = await BodyAsync(response);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("No leave record has been entered for you", body.RootElement.GetProperty("message").GetString());
        Assert.Empty(_repo.Records);
        Assert.Empty(_repo.History);
    }

    [Fact]
    public async Task The_mobile_endpoint_rejects_anonymous_callers_and_admin_tokens_including_the_leave_admin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/leave/my-record", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/leave/my-record", AdminToken(AdminRoleCodes.LeaveAdmin))).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mobile_users_have_no_way_to_create_or_change_leave(string method)
    {
        string token = MobileToken(EmployeeId);

        foreach (string url in new[] { "/api/v1/leave/my-record", "/api/v1/leave", "/api/v1/leave/NSG1001", "/api/v1/leave-records", "/api/v1/leave-uploads" })
        {
            HttpResponseMessage response = await SendAsync(new HttpMethod(method), url, token, Json("{\"nsgNumber\":\"NSG1001\",\"leaveTaken\":1}"));
            Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"{method} {url} -> {(int)response.StatusCode}");
        }

        // and the admin write routes refuse the mobile token outright
        foreach (var route in AdminRoutes().Where(r => r.Method != HttpMethod.Get))
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(route.Method, route.Url, token, route.Content())).StatusCode);

        AssertNothingChanged();
    }

    [Fact]
    public async Task Swagger_documents_every_leave_route_including_the_multipart_upload()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/swagger/v1/swagger.json", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument doc = await BodyAsync(response);
        JsonElement paths = doc.RootElement.GetProperty("paths");
        foreach (string path in new[]
                 {
                     "/api/v1/admin/leave-records", "/api/v1/admin/leave-records/{nsgNumber}", "/api/v1/admin/leave-records/{nsgNumber}/history",
                     "/api/v1/admin/leave-uploads", "/api/v1/admin/leave-uploads/{batchId}", "/api/v1/admin/leave-uploads/{batchId}/errors", "/api/v1/leave/my-record"
                 })
            Assert.True(paths.TryGetProperty(path, out _), $"{path} is missing from the Swagger document");

        Assert.True(paths.GetProperty("/api/v1/admin/leave-uploads").GetProperty("post").GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
    }

    [Fact]
    public void The_leave_admin_role_is_seeded_as_id_7_and_the_policy_admits_only_that_role()
    {
        DbContextOptions<NSG.Mobile.Infrastructure.Persistence.AppDbContext> options = new DbContextOptionsBuilder<NSG.Mobile.Infrastructure.Persistence.AppDbContext>()
            .UseMySql("Server=127.0.0.1;Port=1;Database=model_only;Uid=x;Pwd=x;", new MySqlServerVersion(new Version(8, 0, 36))).Options;
        using NSG.Mobile.Infrastructure.Persistence.AppDbContext context = new(options);   // building the model never connects

        var seeded = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model.FindEntityType(typeof(AdminRole))!.GetSeedData().ToDictionary(r => Convert.ToInt32(r["Id"]), r => Convert.ToString(r["RoleCode"])!);

        Assert.Equal(AdminRoleCodes.LeaveAdmin, seeded[7]);
        Assert.Equal(7, seeded.Count);
        Assert.Equal("LEAVE_ADMIN", AdminRoleCodes.LeaveAdmin);
    }


    // ── Session fakes (the real ones query MySQL) ──

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
