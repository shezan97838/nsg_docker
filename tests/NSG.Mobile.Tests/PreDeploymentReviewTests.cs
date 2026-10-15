using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSG.Mobile.API.Middleware;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Regression tests for the defects found in the pre-deployment review: startup secret validation, the correlation-id header
/// that could silently suppress audit logging, and the audit client address.
/// </summary>
public class PreDeploymentReviewTests
{
    // ───── secrets must be real before the host starts ─────

    private static IConfiguration Config(string? jwt = null, string? encryption = null, string? blind = null, string issuer = "NSG.Mobile.API", string audience = "NSG.Mobile.App")
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = jwt ?? new string('s', 48),
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["Encryption:Key"] = encryption ?? Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
            ["BlindIndex:Key"] = blind ?? Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray())
        }).Build();

    [Fact]
    public void Real_secrets_pass()
        => StartupSecretsValidator.EnsureValid(Config());

    [Theory]
    [InlineData("REPLACE_WITH_SECURE_SECRET_MIN_32_CHARS_LONG_FOR_HMAC_SHA256")]   // the value committed in appsettings.json
    [InlineData("x")]
    [InlineData("0123456789012345678901234567890")]                                 // 31 bytes
    [InlineData("")]
    [InlineData("   ")]
    public void A_placeholder_missing_or_short_jwt_secret_stops_startup_without_echoing_it(string secret)
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(Config(jwt: secret)));
        Assert.Contains("Jwt:Secret", ex.Message);
        if (secret.Trim().Length > 0) Assert.DoesNotContain(secret, ex.Message);
    }

    [Fact]
    public void The_committed_placeholder_values_are_all_rejected()
    {
        // Exactly what a deployment gets if it forgets the environment variables.
        string settings = File.ReadAllText(Path.Combine(FindRoot(), "src", "NSG.Mobile.API", "appsettings.json"));
        IConfiguration committed = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
        Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(committed));
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]                                                            // valid base64, 3 bytes
    public void Bad_encryption_keys_fail_at_startup_not_on_the_first_request(string key)
        => Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(Config(encryption: key)));

    [Fact]
    public void Blind_index_key_must_be_long_enough_and_different_from_the_encryption_key()
    {
        string same = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(Config(encryption: same, blind: same)));
        Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(Config(blind: "AAAA")));
    }

    [Theory]
    [InlineData("REPLACE_ME", "NSG.Mobile.App")]
    [InlineData("NSG.Mobile.API", "")]
    public void Issuer_and_audience_must_be_set(string issuer, string audience)
        => Assert.Throws<InvalidOperationException>(() => StartupSecretsValidator.EnsureValid(Config(issuer: issuer, audience: audience)));

    // ───── correlation id ─────

    private static async Task<string> CorrelationIdSeenAsync(string? header)
    {
        DefaultHttpContext context = new();
        if (header != null) context.Request.Headers["X-Correlation-Id"] = header;
        await new CorrelationIdMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        return context.Items["CorrelationId"]!.ToString()!;
    }

    [Fact]
    public async Task A_reasonable_client_correlation_id_is_kept()
        => Assert.Equal("abc-123_DEF.9", await CorrelationIdSeenAsync("abc-123_DEF.9"));

    [Theory]
    [InlineData("x")]                                // valid but we test the long and odd ones below
    public async Task Short_ids_are_kept(string id) => Assert.Equal(id, await CorrelationIdSeenAsync(id));

    [Fact]
    public async Task Missing_oversized_or_unusual_ids_are_replaced_by_a_generated_one()
    {
        foreach (string? bad in new[] { null, "", new string('a', 65), new string('a', 5000), "has space", "semi;colon", "quote\"", "newéline", "<script>" })
        {
            string used = await CorrelationIdSeenAsync(bad);
            Assert.Matches("^[0-9a-f]{32}$", used);
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NSG.Mobile.API.sln) not found.");
    }
}

/// <summary>Real-MySQL proof that a hostile correlation id can no longer make the audit log silently drop an entry.</summary>
[Collection("mysql")]
public class AuditSurvivesHostileHeadersTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public AuditSurvivesHostileHeadersTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await CleanAsync();
    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    [Fact]
    public async Task A_failed_login_is_audited_even_when_the_client_sends_an_oversized_correlation_id()
    {
        HttpClient client = _factory.CreateClientFor(null);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/mobile-auth/login")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { nsgNumber = "NOBODY-1", mobileNumber = "9876543210", dateOfBirth = "1990-01-01", mpin = "2580" }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Correlation-Id", new string('A', 400));   // audit_logs.correlation_id is varchar(100)

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using AppDbContext ctx = _factory.CreateContext();
        AuditLogEntryView? entry = await ctx.AuditLogs.Where(a => a.Action == AuditAction.LoginFailure)
            .Select(a => new AuditLogEntryView(a.CorrelationId!.Length)).FirstOrDefaultAsync();
        Assert.NotNull(entry);                       // before the fix the insert failed and AuditService swallowed the error: no row
        Assert.True(entry!.CorrelationIdLength <= 100);
    }

    private sealed record AuditLogEntryView(int CorrelationIdLength);
}
