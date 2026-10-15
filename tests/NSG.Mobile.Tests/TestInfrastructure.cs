using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL test environment. Requires env var NSG_TEST_MYSQL pointing at a database whose
/// name ends in "_test" (it is dropped and recreated through migrations). Never point it at dev/prod.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionVariable = "NSG_TEST_MYSQL";
    public string ConnectionString { get; }

    public ApiFactory()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a MySQL connection string for a database named *_test.");

        string database = new MySqlConnector.MySqlConnectionStringBuilder(ConnectionString).Database;
        if (!database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to run: database '{database}' does not end with '_test'.");

        // Environment variables win over appsettings files and are visible to the eager reads in Program.cs.
        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", ConnectionString);

        // Throw-away keys generated in memory for this run (unless the caller, e.g. CI, supplied its own), so the suite
        // never depends on - or reads - a developer's user-secrets or any real key.
        // The Development file turns the temporary authorization bypass on; the suite tests real enforcement, so force it off.
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        SetIfMissing("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        SetIfMissing("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        SetIfMissing("BlindIndex__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // The per-endpoint "auth" (10/min) and "otp" (5 per 5 min) limits would throttle the end-to-end flow tests, which send every
        // request from one loopback address. Production defaults are unchanged; only this test host raises them.
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__OtpPermitLimit", "100000");
    }

    /// <summary>
    /// Test host only: the production global limiter (100 requests/minute/IP) would throttle the whole shared test run,
    /// which issues every request from one loopback address. Production configuration is unchanged.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services => services.PostConfigure<RateLimiterOptions>(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetNoLimiter("test"));
        }));

    private static void SetIfMissing(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            Environment.SetEnvironmentVariable(name, value);
    }

    public AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Drops and recreates the test database by applying every migration (also exercises the migrations on MySQL).</summary>
    public async Task ResetDatabaseAsync()
    {
        await using AppDbContext context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    /// <summary>Empties the geography tables between tests.</summary>
    public async Task ClearGeographyAsync()
    {
        await using AppDbContext context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`"); // references States
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_trade`"); // referenced by UsersPIS
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `pd_trade`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `cities`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `states`");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE `cities` AUTO_INCREMENT = 1");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE `states` AUTO_INCREMENT = 1");
    }

    public HttpClient CreateClientFor(string? role)
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (role != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));
        return client;
    }

    private string CreateToken(string role)
    {
        IConfigurationSection jwt = Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        SigningCredentials creds = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)), SecurityAlgorithms.HmacSha256);
        JwtSecurityToken token = new(
            jwt["Issuer"], jwt["Audience"],
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "9001"), new Claim(ClaimTypes.Role, role) },
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>One migrated database + one host for all geography tests (tests run sequentially).</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.ClearGeographyAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition("mysql")]
public class MySqlCollection : ICollectionFixture<DatabaseFixture> { }
