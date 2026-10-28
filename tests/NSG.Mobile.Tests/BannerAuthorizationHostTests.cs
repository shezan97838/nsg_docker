using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Tests;

/// <summary>
/// The Banner module through the REAL pipeline: real JWT validation (both schemes), real policies, middleware, multipart binding, controller,
/// service, validator, the file result and the envelope filter. Only the outermost edges are in-memory fakes: banner and NSG-unit repositories,
/// private object storage (never real R2), audit and the two session look-ups. A dummy connection string and disabled migrations guarantee
/// nothing can reach MySQL.
/// </summary>
[Collection("GrievanceHost")]
public sealed class BannerAuthorizationHostTests : IDisposable
{
    private static readonly string[] Variables =
    {
        "ConnectionStrings__MySql", "Database__MigrateOnStartup", "Security__DisableAuthorization", "Jwt__Secret",
        "Encryption__Key", "BlindIndex__Key", "ASPNETCORE_ENVIRONMENT"
    };

    private const long AdminId = 9001, EmployeeId = 501;

    private readonly Dictionary<string, string?> _previous = new();
    private readonly BannerFakeRepository _repo = new();
    private readonly GrvFakeStorage _storage = new();
    private readonly GrvAvailability _availability = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeAdminSessions _adminSessions = new();
    private readonly FakeMobileSessions _mobileSessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public BannerAuthorizationHostTests()
    {
        foreach (string name in Variables) _previous[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=127.0.0.1;Port=1;Database=host_check_dummy;Uid=x;Pwd=x;");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        Environment.SetEnvironmentVariable("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("BlindIndex__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBannerRepository>();
            services.AddSingleton<IBannerRepository>(_repo);
            services.RemoveAll<INsgUnitRepository>();
            services.AddSingleton<INsgUnitRepository>(new BannerFakeUnits());
            services.RemoveAll<IFileStorageService>();
            services.AddSingleton<IFileStorageService>(_storage);
            services.RemoveAll<IStorageAvailability>();
            services.AddSingleton<IStorageAvailability>(_availability);
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

    private string AdminToken(string roleCode)
    {
        Guid sid = Guid.NewGuid();
        _adminSessions.Active.Add((sid, AdminId, roleCode));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAdminTokenService>()
            .GenerateAdminAccessToken(new AdminProfile { Id = AdminId }, roleCode, sid).Token;
    }

    private string MobileToken()
    {
        Guid sid = Guid.NewGuid();
        _mobileSessions.Active.Add((sid, EmployeeId));
        using IServiceScope scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateMobileAccessToken(new MobileUserProfile { Id = EmployeeId }, sid).Token;
    }

    // ── Requests ──

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    /// <summary>The multipart form of create/update. <paramref name="image"/> is sent as a file part when given.</summary>
    private static MultipartFormDataContent Form(Dictionary<string, string>? overrides = null, byte[]? image = null, string imageName = "banner.png", string imageType = "image/png")
    {
        Dictionary<string, string> fields = new()
        {
            ["title"] = "Republic Day", ["description"] = "Celebrations", ["category"] = "Directive", ["priority"] = "1", ["isActive"] = "true"
        };
        if (overrides != null) foreach ((string k, string v) in overrides) fields[k] = v;

        MultipartFormDataContent form = new();
        foreach ((string k, string v) in fields) form.Add(new StringContent(v), k);
        if (image != null)
        {
            ByteArrayContent part = new(image);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(imageType);
            form.Add(part, "image", imageName);
        }
        return form;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer, HttpContent? content = null)
    {
        HttpRequestMessage request = new(method, url) { Content = content };
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _factory.CreateClient().SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static readonly (HttpMethod Method, string Url, bool Multipart)[] AdminRoutes =
    {
        (HttpMethod.Get, "/api/v1/admin/banners", false),
        (HttpMethod.Get, "/api/v1/admin/banners/1", false),
        (HttpMethod.Get, "/api/v1/admin/banners/1/image", false),
        (HttpMethod.Post, "/api/v1/admin/banners", true),
        (HttpMethod.Put, "/api/v1/admin/banners/1", true),
        (HttpMethod.Patch, "/api/v1/admin/banners/1/status", false)
    };

    private static HttpContent? BodyFor((HttpMethod Method, string Url, bool Multipart) route)
        => route.Multipart ? Form(new() { ["isActive"] = "true" }, GrvKit.PngBytes)
            : route.Method == HttpMethod.Patch ? Json("{\"isActive\":false}") : null;

    // ── Authorization ──

    [Fact]
    public async Task Every_banner_route_rejects_an_anonymous_caller_with_401()
    {
        foreach (var route in AdminRoutes)
        {
            HttpResponseMessage response = await SendAsync(route.Method, route.Url, null, BodyFor(route));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)response.StatusCode}");
        }
        foreach (string url in new[] { "/api/v1/banners", "/api/v1/banners/1/image" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, url, null)).StatusCode);

        Assert.Empty(_repo.Rows);
        Assert.Empty(_storage.StoreCalls);   // an unauthenticated upload never reaches storage
    }

    [Fact]
    public async Task A_mobile_token_is_refused_on_every_admin_route_and_uploads_nothing()
    {
        string mobile = MobileToken();
        foreach (var route in AdminRoutes)
        {
            HttpResponseMessage response = await SendAsync(route.Method, route.Url, mobile, BodyFor(route));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route.Method} {route.Url} -> {(int)response.StatusCode}");
        }
        Assert.Empty(_repo.Rows);
        Assert.Empty(_storage.StoreCalls);
    }

    [Theory]
    [InlineData(AdminRoleCodes.GrievanceAdmin)]
    [InlineData(AdminRoleCodes.SuperAdmin)]
    [InlineData(AdminRoleCodes.LeaveAdmin)]
    public async Task An_admin_without_the_media_cell_role_gets_403_on_every_admin_route_and_uploads_nothing(string role)
    {
        string token = AdminToken(role);
        foreach (var route in AdminRoutes)
        {
            HttpResponseMessage response = await SendAsync(route.Method, route.Url, token, BodyFor(route));
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} {route.Method} {route.Url} -> {(int)response.StatusCode}");
        }
        Assert.Empty(_repo.Rows);
        Assert.Empty(_storage.StoreCalls);
    }

    [Fact]
    public async Task An_admin_token_is_refused_on_the_mobile_list_and_the_mobile_image()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/banners", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/banners/1/image", admin)).StatusCode);
    }

    // ── Upload, retrieval, replace and remove ──

    [Fact]
    public async Task A_text_only_banner_is_created_listed_and_has_no_image()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        string mobile = MobileToken();

        HttpResponseMessage empty = await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile);
        using (JsonDocument doc = await BodyAsync(empty))
        {
            Assert.Equal("No active banners found.", doc.RootElement.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").ValueKind);
        }

        HttpResponseMessage created = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        int id;
        using (JsonDocument doc = await BodyAsync(created))
        {
            Assert.Equal("Banner created successfully.", doc.RootElement.GetProperty("message").GetString());
            JsonElement data = doc.RootElement.GetProperty("data");
            id = data.GetProperty("id").GetInt32();
            Assert.Equal(JsonValueKind.Null, data.GetProperty("imageUrl").ValueKind);
            Assert.False(data.GetProperty("hasImage").GetBoolean());
            Assert.EndsWith("+05:30", data.GetProperty("createdAt").GetString());
            Assert.True(data.GetProperty("isActive").GetBoolean());
            Assert.Equal("Directive", data.GetProperty("category").GetString());
        }
        Assert.Empty(_storage.StoreCalls);

        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile)))
        {
            JsonElement item = doc.RootElement.GetProperty("data").GetProperty("items")[0];
            Assert.Equal("Republic Day", item.GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("imageUrl").ValueKind);
            Assert.Equal(1, item.GetProperty("priority").GetInt32());
            Assert.True(item.GetProperty("isActive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("nsgUnitId").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("imageAltText").ValueKind);
            Assert.Equal("Directive", item.GetProperty("category").GetString());   // the saved value reaches the mobile JSON
            Assert.False(item.TryGetProperty("startAt", out _));
            Assert.False(item.TryGetProperty("endAt", out _));
            Assert.EndsWith("+05:30", item.GetProperty("updatedAt").GetString());
        }

        // Asking for the image of a text-only banner is a 404 with a message.
        HttpResponseMessage noImage = await SendAsync(HttpMethod.Get, $"/api/v1/banners/{id}/image", mobile);
        Assert.Equal(HttpStatusCode.NotFound, noImage.StatusCode);
        using (JsonDocument doc = await BodyAsync(noImage))
            Assert.Equal("This banner has no image.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_image_uploaded_with_the_banner_is_stored_privately_and_served_to_the_employee_with_a_bearer_token()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        string mobile = MobileToken();

        HttpResponseMessage created = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin,
            Form(new() { ["imageAltText"] = "Parade", ["nsgUnitId"] = "5" }, GrvKit.PngBytes));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        int id;
        string createdBody = await created.Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(createdBody))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            id = data.GetProperty("id").GetInt32();
            Assert.Equal($"http://localhost/api/v1/admin/banners/{id}/image", data.GetProperty("imageUrl").GetString());   // absolute, from the request
            Assert.True(data.GetProperty("hasImage").GetBoolean());
        }

        // One object in private storage under banners/, the row holds only its key, and the key is never in a response.
        string key = Assert.Single(_storage.StoreCalls);
        Assert.StartsWith("banners/", key);
        Assert.Equal(key, _repo.Rows.Single().ImageStorageKey);
        Assert.DoesNotContain(key, createdBody);

        // The mobile list points at the authorized route, not at storage.
        string listBody = await (await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile)).Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(listBody))
        {
            JsonElement item = doc.RootElement.GetProperty("data").GetProperty("items")[0];
            Assert.Equal($"http://localhost/api/v1/banners/{id}/image", item.GetProperty("imageUrl").GetString());
            Assert.Equal("Parade", item.GetProperty("imageAltText").GetString());
        }
        Assert.DoesNotContain(key, listBody);

        // The image itself: authorized, the bytes from storage, the validated type, no sniffing, no caching.
        HttpResponseMessage image = await SendAsync(HttpMethod.Get, $"http://localhost/api/v1/banners/{id}/image", mobile);   // the absolute URL from the list works as is
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(GrvKit.PngBytes, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", image.Headers.GetValues("Cache-Control").Single());

        // The admin can preview it on the admin route (the mobile route does not accept the admin token).
        HttpResponseMessage preview = await SendAsync(HttpMethod.Get, $"/api/v1/admin/banners/{id}/image", admin);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, $"/api/v1/admin/banners/{id}/image", mobile)).StatusCode);
    }


    [Fact]
    public async Task The_mobile_list_orders_by_priority_with_null_last_and_a_configured_public_base_url_is_used_in_the_image_urls()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        string mobile = MobileToken();
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "no-priority", ["priority"] = "" }, GrvKit.PngBytes));
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "p2", ["priority"] = "2" }));
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "p1", ["priority"] = "1" }));
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "hidden", ["priority"] = "0", ["isActive"] = "false" }));

        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile)))
        {
            JsonElement items = doc.RootElement.GetProperty("data").GetProperty("items");
            Assert.Equal(new[] { "p1", "p2", "no-priority" }, items.EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToArray());
            Assert.Equal(JsonValueKind.Null, items[2].GetProperty("priority").ValueKind);
        }

        // A configured PublicApi:BaseUrl (UAT or production host, possibly behind a path prefix) replaces the request-derived address.
        using WebApplicationFactory<Program> configured = _factory.WithWebHostBuilder(b => b.UseSetting("PublicApi:BaseUrl", "https://api.configured.invalid/nsg/"));
        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/banners");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mobile);
        using JsonDocument configuredDoc = await BodyAsync(await configured.CreateClient().SendAsync(request));
        int imageId = _repo.Rows.Single(r => r.Title == "no-priority").Id;
        Assert.Equal($"https://api.configured.invalid/nsg/api/v1/banners/{imageId}/image",
            configuredDoc.RootElement.GetProperty("data").GetProperty("items")[2].GetProperty("imageUrl").GetString());
    }
    [Fact]
    public async Task Deactivating_hides_the_banner_and_its_image_from_employees_but_not_from_the_admin()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        string mobile = MobileToken();
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, GrvKit.PngBytes));
        int id = _repo.Rows.Single().Id;

        HttpResponseMessage off = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/banners/{id}/status", admin, Json("{\"isActive\":false}"));
        using (JsonDocument doc = await BodyAsync(off))
            Assert.Equal("Banner deactivated successfully.", doc.RootElement.GetProperty("message").GetString());

        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile)))
            Assert.Equal("No active banners found.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/v1/banners/{id}/image", mobile)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/v1/admin/banners/{id}/image", admin)).StatusCode);
        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/banners", admin)))
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Put_keeps_the_image_without_a_new_one_replaces_it_with_one_and_removes_it_on_request()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, GrvKit.PngBytes));
        Banner row = _repo.Rows.Single();
        string firstKey = row.ImageStorageKey!;
        string url = $"/api/v1/admin/banners/{row.Id}";

        // 1. Text-only update: the image stays and nothing is deleted.
        HttpResponseMessage keep = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["isActive"] = "true", ["title"] = "Renamed", ["category"] = "Welfare" }));
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Assert.Equal("Renamed", row.Title);
        Assert.Equal("Welfare", row.Category);
        Assert.Equal(firstKey, row.ImageStorageKey);
        Assert.Empty(_storage.Deleted);

        // 2. New image: the reference changes and the old object is deleted.
        HttpResponseMessage replace = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["isActive"] = "true" }, GrvKit.JpegBytes, "new.jpg", "image/jpeg"));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        string secondKey = row.ImageStorageKey!;
        Assert.NotEqual(firstKey, secondKey);
        Assert.EndsWith(".jpg", secondKey);
        Assert.Equal(new[] { firstKey }, _storage.Deleted);
        Assert.Equal(new[] { secondKey }, _storage.Objects.Keys.ToArray());

        // 3. Both a new image and removeImage is a 400 and changes nothing.
        HttpResponseMessage both = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["isActive"] = "true", ["removeImage"] = "true" }, GrvKit.PngBytes));
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(secondKey, row.ImageStorageKey);

        // 4. removeImage: text only again, object deleted, the image route says there is none.
        HttpResponseMessage remove = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["isActive"] = "true", ["removeImage"] = "true" }));
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Null(row.ImageStorageKey);
        Assert.Empty(_storage.Objects);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{url}/image", admin)).StatusCode);
    }

    // ── Failures ──

    [Fact]
    public async Task Bad_input_and_bad_images_use_the_envelope_with_a_message_store_nothing_and_leave_no_object()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);

        HttpResponseMessage invalid = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "" }, GrvKit.PngBytes));
        HttpResponseMessage noCategory = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["category"] = "" }, GrvKit.PngBytes));
        HttpResponseMessage badUnit = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["nsgUnitId"] = "99" }, GrvKit.PngBytes));
        HttpResponseMessage notAnImage = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        HttpResponseMessage wrongType = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, GrvKit.PngBytes, "x.pdf", "application/pdf"));
        HttpResponseMessage missing = await SendAsync(HttpMethod.Get, "/api/v1/admin/banners/404", admin);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badUnit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (HttpResponseMessage response in new[] { invalid, noCategory, badUnit, notAnImage, wrongType, missing })
        {
            using JsonDocument doc = await BodyAsync(response);
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").ValueKind);
        }
        Assert.Empty(_repo.Rows);
        Assert.Empty(_storage.StoreCalls);
        Assert.Empty(_storage.Objects);
    }

    [Fact]
    public async Task A_storage_outage_is_a_503_with_a_message_and_no_banner_is_saved()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        _storage.UnavailableOnStore = true;

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, GrvKit.PngBytes));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using (JsonDocument doc = await BodyAsync(response))
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
        Assert.Empty(_repo.Rows);

        // Text-only banners do not depend on storage.
        HttpResponseMessage textOnly = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form());
        Assert.Equal(HttpStatusCode.Created, textOnly.StatusCode);
    }

    [Fact]
    public async Task An_image_that_is_too_large_is_a_400_and_never_reaches_storage()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        byte[] big = new byte[6 * 1024 * 1024];
        GrvKit.PngBytes.CopyTo(big, 0);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(null, big));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_repo.Rows);
        Assert.Empty(_storage.StoreCalls);
    }

    // ── showOnlyImage over HTTP ──

    [Fact]
    public async Task showOnlyImage_true_without_an_image_is_a_400_with_a_clear_message_on_post_and_put()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);

        HttpResponseMessage post = await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["showOnlyImage"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        using (JsonDocument doc = await BodyAsync(post))
        {
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains("showOnlyImage", doc.RootElement.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").ValueKind);
        }
        Assert.Empty(_repo.Rows);

        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form());                       // a text-only banner
        HttpResponseMessage put = await SendAsync(HttpMethod.Put, $"/api/v1/admin/banners/{_repo.Rows.Single().Id}", admin, Form(new() { ["showOnlyImage"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.False(_repo.Rows.Single().ShowOnlyImage);
    }

    [Fact]
    public async Task The_json_of_every_banner_get_carries_showOnlyImage_as_a_boolean_for_all_display_scenarios()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        string mobile = MobileToken();
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "image-only", ["priority"] = "1", ["showOnlyImage"] = "true" }, GrvKit.PngBytes));
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "image-and-text", ["priority"] = "2", ["showOnlyImage"] = "false" }, GrvKit.PngBytes));
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["title"] = "text-only", ["priority"] = "3" }));
        _repo.Seed("invalid-stored", priority: 4, showOnlyImage: true);   // no image, flag stored as true: must be shown as false

        // Mobile list
        string mobileBody = await (await SendAsync(HttpMethod.Get, "/api/v1/banners", mobile)).Content.ReadAsStringAsync();
        using (JsonDocument doc = JsonDocument.Parse(mobileBody))
        {
            JsonElement[] items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(new[] { "image-only", "image-and-text", "text-only", "invalid-stored" }, items.Select(i => i.GetProperty("title").GetString()).ToArray());
            Assert.Equal(new[] { JsonValueKind.True, JsonValueKind.False, JsonValueKind.False, JsonValueKind.False }, items.Select(i => i.GetProperty("showOnlyImage").ValueKind).ToArray());
            Assert.Equal(new[] { true, true, false, false }, items.Select(i => i.GetProperty("imageUrl").ValueKind == JsonValueKind.String).ToArray());
            Assert.All(items, i => Assert.True(i.GetProperty("imageUrl").ValueKind == JsonValueKind.String || i.GetProperty("showOnlyImage").ValueKind == JsonValueKind.False));
        }
        Assert.Contains("\"showOnlyImage\":true", mobileBody);     // a real JSON boolean, not "true" or 1
        Assert.DoesNotContain("\"showOnlyImage\":\"", mobileBody);

        // Admin list and detail
        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/banners", admin)))
        {
            JsonElement[] items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(4, items.Length);
            Assert.All(items, i => Assert.Contains(i.GetProperty("showOnlyImage").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False }));
            Assert.Equal(JsonValueKind.False, items.Single(i => i.GetProperty("title").GetString() == "invalid-stored").GetProperty("showOnlyImage").ValueKind);
        }
        int imageOnlyId = _repo.Rows.Single(r => r.Title == "image-only").Id;
        using (JsonDocument doc = await BodyAsync(await SendAsync(HttpMethod.Get, $"/api/v1/admin/banners/{imageOnlyId}", admin)))
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("data").GetProperty("showOnlyImage").ValueKind);
    }

    [Fact]
    public async Task Put_preserves_showOnlyImage_with_a_retained_image_and_resets_it_when_the_image_is_removed()
    {
        string admin = AdminToken(AdminRoleCodes.MediaCellAdmin);
        await SendAsync(HttpMethod.Post, "/api/v1/admin/banners", admin, Form(new() { ["showOnlyImage"] = "true" }, GrvKit.PngBytes));
        Banner row = _repo.Rows.Single();
        string url = $"/api/v1/admin/banners/{row.Id}";

        // Retained image, showOnlyImage not sent: stays true.
        HttpResponseMessage keep = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["title"] = "Renamed" }));
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Assert.True(row.ShowOnlyImage);
        using (JsonDocument doc = await BodyAsync(keep))
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("data").GetProperty("showOnlyImage").ValueKind);

        // New image, explicit false.
        HttpResponseMessage replace = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["showOnlyImage"] = "false" }, GrvKit.JpegBytes, "n.jpg", "image/jpeg"));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        Assert.False(row.ShowOnlyImage);

        // Turn it on again, then remove the image: reset to false even though the form still says true -> that is a conflict.
        await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["showOnlyImage"] = "true" }));
        Assert.True(row.ShowOnlyImage);
        HttpResponseMessage conflict = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["removeImage"] = "true", ["showOnlyImage"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
        Assert.True(row.ShowOnlyImage);

        HttpResponseMessage remove = await SendAsync(HttpMethod.Put, url, admin, Form(new() { ["removeImage"] = "true" }));
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Null(row.ImageStorageKey);
        Assert.False(row.ShowOnlyImage);
        using (JsonDocument doc = await BodyAsync(remove))
        {
            Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("data").GetProperty("showOnlyImage").ValueKind);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").GetProperty("imageUrl").ValueKind);
        }
    }

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
