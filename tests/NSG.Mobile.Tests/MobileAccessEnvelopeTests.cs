using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.API.Filters;
using NSG.Mobile.API.Middleware;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAccess;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Tests;

/// <summary>
/// The optional <c>code</c> field of the response envelope, and how the bare 401 of a rejected access token carries
/// <c>SURAKSHA_1001</c>. No database: the real middleware is hosted with tiny inline endpoints.
/// </summary>
public sealed class MobileAccessEnvelopeTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        _app.Run(context =>
        {
            // /{status}/{item}: ends the request bare with that status; "item" is what authentication left in Items.
            string[] parts = context.Request.Path.Value!.Trim('/').Split('/');
            context.Response.StatusCode = int.Parse(parts[0]);
            if (parts.Length > 1) context.Items[DomainConstants.AuthFailure.CodeItemKey] = parts[1];
            return Task.CompletedTask;
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string url)
    {
        using HttpResponseMessage response = await _client.GetAsync(url);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(raw));
        return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone());
    }

    // ───── the record ─────

    [Fact]
    public void Without_a_code_the_envelope_is_byte_for_byte_what_it_always_was()
    {
        Assert.Equal("{\"success\":true,\"message\":\"m\",\"data\":{\"a\":1}}", JsonSerializer.Serialize(ApiResponse.Ok("m", new { a = 1 }), Camel));
        Assert.Equal("{\"success\":false,\"message\":\"m\",\"data\":null}", JsonSerializer.Serialize(ApiResponse.Fail("m"), Camel));
        Assert.Equal("{\"success\":true,\"message\":\"m\",\"data\":null}", JsonSerializer.Serialize(new ApiResponse<object>(true, "m", null), Camel));
    }

    [Fact]
    public void The_block_code_is_added_after_data_when_present()
    {
        Assert.Equal("{\"success\":false,\"message\":\"m\",\"data\":null,\"code\":\"SURAKSHA_1001\"}",
            JsonSerializer.Serialize(ApiResponse.Fail("m", ApiErrorCodes.MobileAccessBlocked), Camel));
        Assert.Equal("{\"success\":true,\"message\":\"m\",\"data\":{\"a\":1},\"code\":\"SURAKSHA_1001\"}",
            JsonSerializer.Serialize(ApiResponse.Ok("m", new { a = 1 }, ApiErrorCodes.MobileAccessBlocked), Camel));
    }

    [Theory]
    [InlineData("TOKEN_INVALID")]
    [InlineData("TOKEN_EXPIRED")]
    [InlineData("TOKEN_REUSE_DETECTED")]
    [InlineData("AUTH_FORBIDDEN")]
    [InlineData("AUTH_ACCOUNT_LOCKED")]
    [InlineData("surakSHA_1001")]
    [InlineData("surakSHA_1002")]
    [InlineData("SURAKSHA_1003")]
    [InlineData("")]
    public void Any_other_code_is_dropped_so_the_field_cannot_leak_internal_codes(string code)
    {
        Assert.DoesNotContain("\"code\"", JsonSerializer.Serialize(ApiResponse.Fail("m", code), Camel));
        Assert.DoesNotContain("\"code\"", JsonSerializer.Serialize(ApiResponse.Ok("m", 1, code), Camel));
    }

    [Fact]
    public void A_problem_body_passes_only_the_public_code_to_the_envelope()
    {
        ProblemDetails blocked = new() { Status = 403, Detail = "blocked" };
        blocked.Extensions["code"] = ApiErrorCodes.MobileAccessBlocked;
        Assert.Equal("SURAKSHA_1001", ApiEnvelope.ForProblem(blocked, 403).Code);

        ProblemDetails other = new() { Status = 401, Detail = "x" };
        other.Extensions["code"] = ApiErrorCodes.RefreshTokenInvalid;
        Assert.Null(ApiEnvelope.ForProblem(other, 401).Code);

        Assert.Null(ApiEnvelope.ForProblem(new ProblemDetails { Status = 404, Detail = "x", Title = "TOKEN_INVALID" }, 404).Code);   // Title is never a code
    }

    [Fact]
    public void A_success_carrier_passes_its_code_and_ordinary_results_have_none()
    {
        Assert.Null(new ApiResult<int>("m", 1).Code);
        IApiMessageCarrier carrier = new ApiResult<int>("m", 1) { Code = ApiErrorCodes.MobileAccessBlocked };
        Assert.Equal("SURAKSHA_1001", carrier.Code);
        Assert.Null(((IApiMessageCarrier)new ApiMessageResponse("m")).Code);
    }

    // ───── the bare 401 of a rejected access token ─────

    [Fact]
    public async Task A_rejected_access_token_of_a_blocked_profile_gets_the_code_in_a_401_body()
    {
        var (status, body) = await GetAsync("/401/" + ApiErrorCodes.MobileAccessBlocked);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal("SURAKSHA_1001", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_plain_401_stays_exactly_as_before()
    {
        var (status, body) = await GetAsync("/401");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(new[] { "success", "message", "data" }, body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Authentication is required. Please sign in again.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("/401/TOKEN_INVALID")]
    [InlineData("/401/SURAKSHA_1003")]
    [InlineData("/401/surakSHA_1002")]
    [InlineData("/401/")]
    public async Task Nothing_but_the_two_session_codes_in_Items_is_honoured(string url)
    {
        var (status, body) = await GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.False(body.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task A_rejected_access_token_of_a_replaced_session_gets_1002_in_a_401_body()
    {
        var (status, body) = await GetAsync("/401/" + ApiErrorCodes.MobileSessionReplaced);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("Your session has been terminated because your account was signed in on another device.", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal("SURAKSHA_1002", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(403, ApiErrorCodes.MobileAccessBlocked)]
    [InlineData(404, ApiErrorCodes.MobileAccessBlocked)]
    [InlineData(429, ApiErrorCodes.MobileAccessBlocked)]
    [InlineData(403, ApiErrorCodes.MobileSessionReplaced)]
    [InlineData(404, ApiErrorCodes.MobileSessionReplaced)]
    [InlineData(429, ApiErrorCodes.MobileSessionReplaced)]
    public async Task The_session_codes_are_only_ever_written_for_a_401(int status, string code)
    {
        var (actual, body) = await GetAsync($"/{status}/{code}");
        Assert.Equal(status, (int)actual);
        Assert.False(body.TryGetProperty("code", out _));
    }

    [Fact]
    public void A_problem_body_passes_1002_to_the_envelope_and_the_two_codes_have_distinct_messages()
    {
        ProblemDetails replaced = new() { Status = 403, Detail = "x" };
        replaced.Extensions["code"] = ApiErrorCodes.MobileSessionReplaced;
        Assert.Equal("SURAKSHA_1002", ApiEnvelope.ForProblem(replaced, 403).Code);

        Assert.NotEqual(MobileAuthMessages.AccessBlockedByPisAdmin, MobileAuthMessages.SessionReplacedByNewLogin);
        Assert.Equal(MobileAuthMessages.AccessBlockedByPisAdmin, MobileAuthMessages.ForSessionCode(ApiErrorCodes.MobileAccessBlocked));
        Assert.Equal(MobileAuthMessages.SessionReplacedByNewLogin, MobileAuthMessages.ForSessionCode(ApiErrorCodes.MobileSessionReplaced));
        Assert.Null(MobileAuthMessages.ForSessionCode("TOKEN_INVALID"));
    }

    // ───── authorization wiring of the new controller ─────

    [Fact]
    public void The_admin_controller_is_protected_by_the_portal_only_pis_admin_policy_and_nothing_is_anonymous()
    {
        Type controller = typeof(MobileAccessAdminController);
        AuthorizeAttribute authorize = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(DomainConstants.Policies.RequirePisAdminPortal, authorize.Policy);
        Assert.NotEqual(DomainConstants.Policies.RequirePisAdmin, authorize.Policy);      // that one also admits the staff Bearer path
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.All(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
        Assert.Equal("api/v1/admin/mobile-access", controller.GetCustomAttribute<RouteAttribute>()!.Template);

        (string Method, Type Verb, string Template)[] expected =
        {
            (nameof(MobileAccessAdminController.Get), typeof(HttpGetAttribute), "{nsgNumber}"),
            (nameof(MobileAccessAdminController.Block), typeof(HttpPutAttribute), "{nsgNumber}/block"),
            (nameof(MobileAccessAdminController.Restore), typeof(HttpPutAttribute), "{nsgNumber}/restore")
        };
        foreach ((string method, Type verb, string template) in expected)
        {
            HttpMethodAttribute attribute = (HttpMethodAttribute)controller.GetMethod(method)!.GetCustomAttribute(verb)!;
            Assert.Equal(template, attribute.Template);
        }
    }

    [Fact]
    public void The_policy_constant_is_new_and_distinct()
    {
        Assert.Equal("RequirePisAdminPortal", DomainConstants.Policies.RequirePisAdminPortal);
        Assert.NotEqual(DomainConstants.Policies.RequirePisAdmin, DomainConstants.Policies.RequirePisAdminPortal);
    }
}
