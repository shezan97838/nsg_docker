using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.API.Security;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>
/// Cross-platform readiness checks that run anywhere (no database): path handling, trusted-proxy handling, configuration hygiene.
/// They run on whatever OS the suite runs on; they are NOT a substitute for running the application on Ubuntu.
/// </summary>
public class LinuxReadinessTests
{
    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NSG.Mobile.API.sln) not found.");
    }

    // ───── file storage ─────

    [Fact]
    public void Inside_check_compares_whole_segments_and_follows_the_file_system_case_rules()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "nsg-files");
        Assert.True(LocalFileStorageService.IsInside(baseDir, Path.Combine(baseDir, "a", "b.pdf")));
        Assert.True(LocalFileStorageService.IsInside(baseDir + Path.DirectorySeparatorChar, Path.Combine(baseDir, "b.pdf")));
        Assert.False(LocalFileStorageService.IsInside(baseDir, baseDir + "-evil" + Path.DirectorySeparatorChar + "x.pdf"));   // sibling with the same prefix
        Assert.False(LocalFileStorageService.IsInside(baseDir, baseDir));                                                      // the root itself is not a file inside it
        Assert.False(LocalFileStorageService.IsInside(baseDir, Path.GetFullPath(Path.Combine(baseDir, "..", "other.pdf"))));

        string differentCase = Path.Combine(baseDir.ToUpperInvariant(), "x.pdf");
        Assert.Equal(OperatingSystem.IsWindows(), LocalFileStorageService.IsInside(baseDir, differentCase));   // Linux file systems are case-sensitive
    }

    [Fact]
    public async Task Storing_a_file_stays_inside_the_root_and_a_traversal_category_creates_nothing_outside()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "nsg-storage-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(sandbox, "root");
        try
        {
            LocalFileStorageService storage = new(Options.Create(new FileStorageOptions { BasePath = root }), NullLogger<LocalFileStorageService>.Instance);

            await using (MemoryStream ok = new(new byte[] { 1, 2, 3 }))
            {
                string key = await storage.StoreAsync(ok, "report.pdf", "application/pdf", "docs");
                Assert.StartsWith("docs/", key);                         // forward slashes in keys on every OS
                Assert.True(File.Exists(Path.Combine(root, key)));
            }

            await using MemoryStream evil = new(new byte[] { 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.StoreAsync(evil, "x.pdf", "application/pdf", "../../escaped"));
            Assert.False(Directory.Exists(Path.Combine(sandbox, "escaped")));
            Assert.False(Directory.Exists(Path.Combine(sandbox, "..", "escaped")));
            Assert.Null(await storage.GetAsync("../../outside.pdf"));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public void Committed_configuration_has_no_windows_paths_and_no_unset_file_storage_default()
    {
        string settings = File.ReadAllText(Path.Combine(Root(), "src", "NSG.Mobile.API", "appsettings.json"));
        Assert.DoesNotMatch(new Regex(@"""[A-Za-z]:\\\\"), settings);   // no "C:\\..." value
        Assert.Contains("\"BasePath\": \"\"", settings);                 // the environment must supply it

        string development = File.ReadAllText(Path.Combine(Root(), "src", "NSG.Mobile.API", "appsettings.Development.json"));
        Assert.DoesNotMatch(new Regex(@"[A-Za-z]:\\"), development);
        Assert.DoesNotContain("\\", development.Replace("\\\"", ""));   // relative, forward-slash path for local development
    }

    // ───── publish and startup hygiene (static) ─────

    [Fact]
    public void Development_settings_are_excluded_from_the_published_package()
    {
        string csproj = File.ReadAllText(Path.Combine(Root(), "src", "NSG.Mobile.API", "NSG.Mobile.API.csproj"));
        Assert.Contains("<Content Update=\"appsettings.Development.json\" CopyToPublishDirectory=\"Never\" />", csproj);
    }

    [Fact]
    public void The_application_applies_migrations_only_through_the_opt_in_startup_migrator()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            string separator = Path.DirectorySeparatorChar.ToString();
            if (file.Contains(separator + "Migrations" + separator) || file.Contains(separator + "obj" + separator) || file.Contains(separator + "bin" + separator)) continue;
            if (Path.GetFileName(file) == "StartupDatabaseMigrator.cs") continue;   // the single, opt-in exception (Database__MigrateOnStartup, default off)
            string text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"\.(Migrate|MigrateAsync|EnsureCreated|EnsureCreatedAsync)\s*\("), text);
        }
    }

    [Fact]
    public void Startup_migration_is_controlled_by_the_Database_MigrateOnStartup_setting()
    {
        string program = File.ReadAllText(Path.Combine(Root(), "src", "NSG.Mobile.API", "Program.cs"));
        Assert.Contains("if (builder.Configuration.GetValue<bool>(StartupDatabaseMigrator.SettingName))", program);
        Assert.Equal("Database:MigrateOnStartup", NSG.Mobile.API.Security.StartupDatabaseMigrator.SettingName);

        // The switch lives in appsettings.json (an environment variable Database__MigrateOnStartup=false overrides it).
        using System.Text.Json.JsonDocument settings = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Root(), "src", "NSG.Mobile.API", "appsettings.json")));
        Assert.True(settings.RootElement.GetProperty("Database").TryGetProperty("MigrateOnStartup", out System.Text.Json.JsonElement value));
        Assert.True(value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False);
    }

    [Fact]
    public void Source_and_project_files_reference_each_other_with_exact_case()
    {
        // Linux file names are case-sensitive: every <ProjectReference> must name a file that exists with that exact spelling.
        foreach (string csproj in Directory.GetFiles(Path.Combine(Root(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(csproj), @"ProjectReference Include=""([^""]+)"""))
            {
                string referenced = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
                string directory = Path.GetDirectoryName(referenced)!;
                Assert.Contains(Path.GetFileName(referenced), Directory.GetFiles(directory).Select(Path.GetFileName));   // exact-case match
            }
        }
    }

    // ───── demo database isolation ─────

    [Theory]
    [InlineData("Staging", "Server=db;Database=nsg_hosted_demo;Uid=u;Pwd=p")]
    [InlineData("Demo", "Server=db;Database=NSG_Hosted_DEMO;Uid=u;Pwd=p")]
    [InlineData("Development", "Server=db;Database=nsg_mobile_dev;Uid=u;Pwd=p")]
    public void Demo_mode_accepts_a_dedicated_demo_database(string environment, string connection)
        => OtpModeGuard.EnsureDemoDatabase(new OtpOptions { Mode = OtpMode.Demo, AllowDemoOutsideDevelopment = true }, environment, connection);

    [Theory]
    [InlineData("Server=db;Database=nsg_mobile_prod;Uid=u;Pwd=p")]
    [InlineData("Server=db;Database=nsg_uat;Uid=u;Pwd=p")]
    [InlineData("")]
    [InlineData(null)]
    public void Demo_mode_accepts_any_database_by_owner_decision(string? connection)
        => OtpModeGuard.EnsureDemoDatabase(new OtpOptions { Mode = OtpMode.Demo }, "Production", connection);

    [Fact]
    public void Sms_mode_has_no_database_restriction()
        => OtpModeGuard.EnsureDemoDatabase(new OtpOptions { Mode = OtpMode.Sms }, "Production", "Server=db;Database=nsg_mobile_prod");

    // ───── forwarded headers ─────

    [Fact]
    public void Forwarded_headers_are_off_by_default_and_misconfiguration_stops_startup()
    {
        Assert.Null(ForwardedHeadersSetup.Build(new TrustedProxySettings()));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(new TrustedProxySettings { Enabled = true }));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(new TrustedProxySettings { Enabled = true, KnownProxies = new[] { "not-an-ip" } }));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(new TrustedProxySettings { Enabled = true, KnownNetworks = new[] { "10.0.0.0" } }));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(new TrustedProxySettings { Enabled = true, KnownNetworks = new[] { "10.0.0.0/40" } }));
    }

    [Fact]
    public void Only_the_listed_proxies_are_trusted_not_loopback_by_default()
    {
        var options = ForwardedHeadersSetup.Build(new TrustedProxySettings { Enabled = true, KnownProxies = new[] { "10.0.0.5" }, KnownNetworks = new[] { "192.168.1.0/24" } })!;
        Assert.Equal(new[] { IPAddress.Parse("10.0.0.5") }, options.KnownProxies);
        Assert.Single(options.KnownNetworks);
        Assert.DoesNotContain(IPAddress.Loopback, options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }

    private static async Task<string> CallThroughProxyAsync(TrustedProxySettings settings, string connectingAddress, bool sendForwardedHeaders = true)
    {
        var options = ForwardedHeadersSetup.Build(settings)!;
        using IHost host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .Configure(app =>
            {
                app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(connectingAddress); return next(); });
                app.UseForwardedHeaders(options);
                app.Run(context => context.Response.WriteAsync($"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}"));
            })).StartAsync();

        HttpClient client = host.GetTestClient();
        if (sendForwardedHeaders)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        }
        return await client.GetStringAsync("/");
    }

    [Fact]
    public async Task A_trusted_proxy_supplies_the_real_scheme_and_client_address_so_https_redirects_and_rate_limits_see_the_client()
    {
        TrustedProxySettings settings = new() { Enabled = true, KnownProxies = new[] { "10.0.0.5" } };
        Assert.Equal("https|203.0.113.9", await CallThroughProxyAsync(settings, "10.0.0.5"));
    }

    [Fact]
    public async Task Forwarded_headers_from_anyone_else_are_ignored_including_loopback_unless_listed()
    {
        TrustedProxySettings settings = new() { Enabled = true, KnownProxies = new[] { "10.0.0.5" } };
        Assert.Equal("http|198.51.100.7", await CallThroughProxyAsync(settings, "198.51.100.7"));   // a client faking the headers
        Assert.Equal("http|127.0.0.1", await CallThroughProxyAsync(settings, "127.0.0.1"));         // loopback is not trusted unless configured
        Assert.Equal("http|127.0.0.1", await CallThroughProxyAsync(new TrustedProxySettings { Enabled = true, KnownProxies = new[] { "127.0.0.1" } }, "127.0.0.1", sendForwardedHeaders: false));
        Assert.Equal("https|203.0.113.9", await CallThroughProxyAsync(new TrustedProxySettings { Enabled = true, KnownProxies = new[] { "127.0.0.1" } }, "127.0.0.1"));   // nginx on the same host, listed explicitly
    }
}
