using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using NSG.Mobile.API.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// The temporary authorization bypass (Security:DisableAuthorization) must work without a token in Development, must be
/// impossible to enable anywhere else, and must be off by default so the rest of the suite tests real enforcement.
/// </summary>
[Collection("mysql")]
public class AuthorizationBypassTests
{
    private const string EnvVariable = "Security__DisableAuthorization";

    public AuthorizationBypassTests(DatabaseFixture _) { }   // ensures the migrated test database exists

    private static void WithBypass(string value, Action action)
    {
        string? previous = Environment.GetEnvironmentVariable(EnvVariable);
        try
        {
            Environment.SetEnvironmentVariable(EnvVariable, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVariable, previous);
        }
    }

    private static string RepoFile(params string[] parts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln")))
            dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public async Task With_the_flag_on_in_development_endpoints_work_without_a_token()
    {
        HttpStatusCode pis = default, ranks = default, write = default;
        WithBypass("true", () =>
        {
            using ApiFactory factory = new();
            Environment.SetEnvironmentVariable(EnvVariable, "true");   // ApiFactory's constructor resets it to false
            using HttpClient anonymous = factory.CreateClientFor(null);
            pis = anonymous.GetAsync("/api/v1/users-pis").GetAwaiter().GetResult().StatusCode;
            ranks = anonymous.GetAsync("/api/v1/ranks").GetAwaiter().GetResult().StatusCode;   // PIS Admin endpoint
            write = anonymous.DeleteAsync("/api/v1/users-pis/424242").GetAwaiter().GetResult().StatusCode;
        });
        await Task.CompletedTask;

        Assert.Equal(HttpStatusCode.OK, pis);
        Assert.Equal(HttpStatusCode.OK, ranks);
        Assert.Equal(HttpStatusCode.NotFound, write);   // reached the handler (record does not exist) instead of 401/403
    }

    [Fact]
    public void With_the_flag_off_the_same_requests_are_rejected_with_401()
    {
        HttpStatusCode pis = default, ranks = default;
        WithBypass("false", () =>
        {
            using ApiFactory factory = new();
            using HttpClient anonymous = factory.CreateClientFor(null);
            pis = anonymous.GetAsync("/api/v1/users-pis").GetAwaiter().GetResult().StatusCode;
            ranks = anonymous.GetAsync("/api/v1/ranks").GetAwaiter().GetResult().StatusCode;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, pis);
        Assert.Equal(HttpStatusCode.Unauthorized, ranks);
    }

    [Fact]
    public void The_flag_cannot_be_enabled_outside_development()
    {
        Exception? failure = null;
        WithBypass("false", () =>
        {
            using ApiFactory factory = new();
            Environment.SetEnvironmentVariable(EnvVariable, "true");
            // Outside Development the host also insists on an absolute FileStorage root; give it one so the bypass check is what fails.
            Environment.SetEnvironmentVariable("FileStorage__BasePath", Path.GetTempPath());
            using WebApplicationFactoryHolder holder = new(factory, "Staging");
            failure = Record.Exception(() => holder.CreateClient());
            Environment.SetEnvironmentVariable("FileStorage__BasePath", null);
        });

        Assert.NotNull(failure);
        List<string> messages = new();
        for (Exception? e = failure; e != null; e = e.InnerException)
            messages.Add(e.Message);
        Assert.Contains(messages, m => m.Contains("may only be enabled in the Development environment"));
    }

    [Fact]
    public async Task Handler_satisfies_every_pending_requirement_including_role_and_deny_anonymous()
    {
        AuthorizationHandlerContext context = new(
            new IAuthorizationRequirement[] { new DenyAnonymousAuthorizationRequirement(), new RolesAuthorizationRequirement(new[] { "PisAdmin" }) },
            new ClaimsPrincipal(new ClaimsIdentity()),   // anonymous, no roles
            resource: null);

        await new DevelopmentAuthorizationBypassHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Empty(context.PendingRequirements);
    }

    [Fact]
    public void Only_the_development_settings_file_turns_the_bypass_on()
    {
        using JsonDocument dev = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "NSG.Mobile.API", "appsettings.Development.json")));
        using JsonDocument baseSettings = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "NSG.Mobile.API", "appsettings.json")));

        Assert.True(dev.RootElement.GetProperty("Security").GetProperty("DisableAuthorization").GetBoolean());

        bool baseValue = baseSettings.RootElement.TryGetProperty("Security", out JsonElement security)
            && security.TryGetProperty("DisableAuthorization", out JsonElement flag) && flag.GetBoolean();
        Assert.False(baseValue);
    }

    /// <summary>Thin wrapper so the test can switch the hosting environment of an <see cref="ApiFactory"/>.</summary>
    private sealed class WebApplicationFactoryHolder : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;

        public WebApplicationFactoryHolder(ApiFactory factory, string environment)
            => _factory = factory.WithWebHostBuilder(b => b.UseEnvironment(environment));

        public HttpClient CreateClient() => _factory.CreateClient();

        public void Dispose() => _factory.Dispose();
    }
}
