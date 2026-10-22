using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSG.Mobile.API.Filters;
using NSG.Mobile.API.Middleware;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Tests;

/// <summary>
/// The global response envelope <c>{ success, message, data }</c>. These tests need no database: they host the real
/// <see cref="ApiResponseEnvelopeFilter"/> and <see cref="GlobalExceptionHandlingMiddleware"/> with small probe controllers.
/// </summary>
public sealed class ApiResponseEnvelopeTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(o => o.Filters.Add<ApiResponseEnvelopeFilter>())
            .AddApplicationPart(typeof(EnvelopeProbeController).Assembly);
        builder.Services.AddAuthentication("probe").AddScheme<AuthenticationSchemeOptions, DenyAllHandler>("probe", _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> Send(HttpMethod method, string url, object? json = null)
    {
        using HttpRequestMessage request = new(method, url);
        if (json != null) request.Content = JsonContent.Create(json);
        using HttpResponseMessage response = await _client.SendAsync(request);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(raw), $"{method} {url} returned a blank body ({(int)response.StatusCode}).");
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType + "");
        return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    /// <summary>Every body has exactly success, message and data, in that shape.</summary>
    private static void AssertEnvelope(JsonElement body, bool success)
    {
        Assert.Equal(new[] { "success", "message", "data" }, body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(success, body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        if (!success) Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Detail_response_is_wrapped_with_object_data()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/probe/item");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertEnvelope(body, true);
        Assert.Equal("Request completed successfully", body.GetProperty("message").GetString());
        Assert.Equal("Alpha", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task List_response_has_array_data_even_when_empty()
    {
        var (_, body, _) = await Send(HttpMethod.Get, "/probe/list");
        AssertEnvelope(body, true);
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());

        var (_, empty, _) = await Send(HttpMethod.Get, "/probe/empty-list");
        AssertEnvelope(empty, true);
        Assert.Equal(JsonValueKind.Array, empty.GetProperty("data").ValueKind);
        Assert.Equal(0, empty.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Create_keeps_201_and_location_header()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/probe/create", new { name = "x" });
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        AssertEnvelope(body, true);
        Assert.Equal("Resource created successfully", body.GetProperty("message").GetString());
        Assert.Equal(7, body.GetProperty("data").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Bodyless_204_becomes_200_with_null_data()
    {
        var (status, body, _) = await Send(HttpMethod.Delete, "/probe/item/1");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertEnvelope(body, true);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Service_message_and_payload_are_flattened_into_the_envelope()
    {
        var (_, result, _) = await Send(HttpMethod.Put, "/probe/item/1", new { name = "Beta" });
        AssertEnvelope(result, true);
        Assert.Equal("Probe updated successfully.", result.GetProperty("message").GetString());
        Assert.Equal("Beta", result.GetProperty("data").GetProperty("name").GetString());

        var (_, message, _) = await Send(HttpMethod.Patch, "/probe/item/1");
        AssertEnvelope(message, true);
        Assert.Equal("Probe is already inactive.", message.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task An_existing_envelope_is_not_wrapped_twice()
    {
        var (_, body, _) = await Send(HttpMethod.Get, "/probe/already-wrapped");
        AssertEnvelope(body, true);
        Assert.Equal("Already wrapped", body.GetProperty("message").GetString());
        Assert.Equal("Alpha", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Problem_results_keep_their_status_and_become_failure_envelopes()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/probe/not-found");
        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertEnvelope(body, false);
        Assert.Equal("Probe not found.", body.GetProperty("message").GetString());

        var (plainStatus, plain, _) = await Send(HttpMethod.Get, "/probe/plain-not-found");
        Assert.Equal(HttpStatusCode.NotFound, plainStatus);
        AssertEnvelope(plain, false);
    }

    [Fact]
    public async Task Validation_failure_is_a_400_envelope_listing_the_field_messages()
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/probe/validate", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertEnvelope(body, false);
        Assert.Contains("Name is required.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unauthenticated_request_is_a_401_envelope()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/probe/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        AssertEnvelope(body, false);
    }

    [Fact]
    public async Task Unknown_route_is_a_404_envelope()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/probe/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertEnvelope(body, false);
    }

    [Fact]
    public async Task Unexpected_exception_is_a_500_envelope_without_internal_details()
    {
        var (status, body, raw) = await Send(HttpMethod.Get, "/probe/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, status);
        AssertEnvelope(body, false);
        Assert.Equal("An unexpected error occurred.", body.GetProperty("message").GetString());
        Assert.DoesNotContain("SELECT", raw);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", raw);
        Assert.DoesNotContain("stackTrace", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Domain_exceptions_map_to_their_status_with_the_envelope()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/probe/throw-not-found");
        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertEnvelope(body, false);
        Assert.Equal("Probe with ID '9' was not found.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task File_downloads_are_not_wrapped()
    {
        using HttpResponseMessage response = await _client.GetAsync("/probe/file");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("raw-bytes", await response.Content.ReadAsStringAsync());
    }

    private sealed class DenyAllHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public DenyAllHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}

public sealed record ProbeDto(int Id, string Name);

public sealed class ProbeRequest
{
    [Required(ErrorMessage = "Name is required.")]
    public string? Name { get; set; }
}

[ApiController]
[Route("probe")]
public sealed class EnvelopeProbeController : ControllerBase
{
    [HttpGet("item")] public IActionResult Item() => Ok(new ProbeDto(1, "Alpha"));

    [HttpGet("list")] public IActionResult List() => Ok(new[] { new ProbeDto(1, "Alpha"), new ProbeDto(2, "Beta") });

    [HttpGet("empty-list")] public IActionResult EmptyList() => Ok(Array.Empty<ProbeDto>());

    [HttpPost("create")]
    public IActionResult Create([FromBody] ProbeRequest request) => CreatedAtAction(nameof(Item), new ProbeDto(7, request.Name ?? ""));

    [HttpDelete("item/{id:int}")] public IActionResult Delete(int id) => NoContent();

    [HttpPut("item/{id:int}")]
    public IActionResult Update(int id, [FromBody] ProbeRequest request)
        => Ok(new ApiResult<ProbeDto>("Probe updated successfully.", new ProbeDto(id, request.Name ?? "")));

    [HttpPatch("item/{id:int}")] public IActionResult Deactivate(int id) => Ok(new ApiMessageResponse("Probe is already inactive."));

    [HttpGet("already-wrapped")]
    public IActionResult AlreadyWrapped() => Ok(new ApiResponse<ProbeDto>(true, "Already wrapped", new ProbeDto(1, "Alpha")));

    [HttpGet("not-found")]
    public IActionResult NotFoundProblem() => NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Probe not found." });

    [HttpGet("plain-not-found")] public IActionResult PlainNotFound() => NotFound();

    [HttpPost("validate")] public IActionResult Validate([FromBody] ProbeRequest request) => Ok(request);

    [HttpGet("secure")]
    [Authorize]
    public IActionResult Secure() => Ok();

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("SELECT * FROM users WHERE password = 'x'");

    [HttpGet("throw-not-found")]
    public IActionResult ThrowNotFound() => throw new NSG.Mobile.Domain.Exceptions.EntityNotFoundException("Probe", "9");

    [HttpGet("file")]
    public IActionResult File() => base.File(System.Text.Encoding.UTF8.GetBytes("raw-bytes"), "text/plain");
}
