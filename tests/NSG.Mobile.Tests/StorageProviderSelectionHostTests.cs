using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Services;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Tests;

/// <summary>
/// Which storage the real host wires up (always the local file system) and what its startup check refuses. The host is started in-process with a dummy connection string and
/// migrations off (nothing reaches MySQL); <c>Storage:RootPath</c> is supplied through an
/// in-memory configuration source so a test can make it blank, which an environment variable cannot do on Windows.
/// Policy under test: local file storage is the only storage, in every environment; it always needs <c>Storage:RootPath</c> and never falls back to another root. Stray R2 settings are ignored.
/// </summary>
[Collection("GrievanceHost")]
public sealed class StorageProviderSelectionHostTests : IDisposable
{
    private static readonly string[] Variables =
    {
        "ConnectionStrings__MySql", "Database__MigrateOnStartup", "Security__DisableAuthorization", "Jwt__Secret", "Encryption__Key",
        "ASPNETCORE_ENVIRONMENT", "Storage__RootPath", "FileStorage__BasePath", "Form16Storage__RootPath", "SalarySlipStorage__RootPath",
        "R2Storage__ServiceUrl", "R2Storage__BucketName", "R2Storage__AccessKey", "R2Storage__SecretKey"
    };

    private readonly Dictionary<string, string?> _previous = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-provider-tests-" + Guid.NewGuid().ToString("N"));

    public StorageProviderSelectionHostTests()
    {
        foreach (string name in Variables) _previous[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=127.0.0.1;Port=1;Database=provider_check_dummy;Uid=x;Pwd=x;");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Security__DisableAuthorization", "false");
        Environment.SetEnvironmentVariable("Jwt__Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Encryption__Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        foreach (string legacy in new[] { "FileStorage__BasePath", "Form16Storage__RootPath", "SalarySlipStorage__RootPath", "R2Storage__ServiceUrl", "R2Storage__BucketName", "R2Storage__AccessKey", "R2Storage__SecretKey" })
            Environment.SetEnvironmentVariable(legacy, null);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _previous) Environment.SetEnvironmentVariable(name, value);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static void ConfigureR2()
    {
        Environment.SetEnvironmentVariable("R2Storage__ServiceUrl", "https://acct.r2.cloudflarestorage.com");
        Environment.SetEnvironmentVariable("R2Storage__BucketName", "bucket-that-does-not-exist");
        Environment.SetEnvironmentVariable("R2Storage__AccessKey", "fake-access-key");
        Environment.SetEnvironmentVariable("R2Storage__SecretKey", "fake-secret-key");
    }

    /// <summary>A host in <paramref name="environment"/> whose <c>Storage:RootPath</c> is <paramref name="rootPath"/> (null = leave whatever configuration says).</summary>
    private static WebApplicationFactory<Program> Host(string environment, string? rootPath)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            if (rootPath != null) b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:RootPath"] = rootPath }));
        });

    private static T Get<T>(WebApplicationFactory<Program> factory) where T : notnull
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private static List<string> StartupFailure(WebApplicationFactory<Program> factory)
    {
        Exception? failure = Record.Exception(() =>
        {
            using (factory) _ = factory.Services;
        });

        Assert.NotNull(failure);
        List<string> messages = new();
        for (Exception? e = failure; e != null; e = e.InnerException) messages.Add(e.Message);
        return messages;
    }

    // ── Local file storage: Storage:RootPath is mandatory ──

    [Fact]
    public void Development_with_a_valid_root_starts_and_all_five_modules_use_it()
    {
        using WebApplicationFactory<Program> factory = Host("Development", _root);

        Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
        Assert.IsType<Form16StorageService>(Get<IForm16StorageService>(factory));
        Assert.IsType<SalarySlipStorageService>(Get<ISalarySlipStorageService>(factory));
        IStorageRoot root = factory.Services.GetRequiredService<IStorageRoot>();
        Assert.True(root.IsConfigured);
        foreach (StorageModule module in StorageModules.All)
        {
            Assert.Equal(Path.Combine(Path.GetFullPath(_root), StorageModules.FolderName(module)), root.ModuleRoot(module));
            Assert.True(Directory.Exists(root.ModuleRoot(module)), module + " folder");
        }

        Assert.True(Directory.Exists(Path.Combine(_root, "_incoming")));
    }


    [Fact]
    public void The_committed_Development_root_is_D_NSGStorage_and_validates_when_that_folder_exists()
    {
        string configured = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json")).Build()["Storage:RootPath"]!;
        Assert.Equal(@"D:\NSGStorage", configured);
        if (!OperatingSystem.IsWindows() || !Directory.Exists(configured)) return;   // the rest is only meaningful on the development machine

        // Validation only creates missing module folders and writes/deletes one probe file; existing documents are never touched.
        IStorageRoot root = TestStorage.CentralUnchecked(configured);
        root.Validate();

        foreach (StorageModule module in StorageModules.All)
            Assert.Equal(Path.Combine(configured, StorageModules.FolderName(module)), root.ModuleRoot(module));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Development_without_a_root_does_not_start_and_does_not_fall_back_to_anything(string blank)
    {
        // Legacy per-module roots are set to valid folders; they must not rescue the host.
        Environment.SetEnvironmentVariable("FileStorage__BasePath", _root);
        Environment.SetEnvironmentVariable("Form16Storage__RootPath", Path.Combine(_root, "f16"));
        Environment.SetEnvironmentVariable("SalarySlipStorage__RootPath", Path.Combine(_root, "slips"));

        List<string> messages = StartupFailure(Host("Development", blank));

        Assert.Contains(messages, m => m.Contains("Storage:RootPath is not configured"));
        Assert.False(Directory.Exists(Path.Combine(_root, "f16")) || Directory.Exists(Path.Combine(_root, "slips")) || Directory.Exists(Path.Combine(_root, "Form16")));
    }

    [Theory]
    [InlineData("relative-storage", "absolute")]
    [InlineData("missing", "does not exist")]
    public void Development_with_an_invalid_or_unavailable_root_does_not_start(string kind, string expected)
    {
        string configured = kind == "missing" ? Path.Combine(_root, "not-mounted") : kind;

        List<string> messages = StartupFailure(Host("Development", configured));

        Assert.Contains(messages, m => m.Contains("Storage:RootPath") && m.Contains(expected));
        Assert.False(Directory.Exists(Path.Combine(_root, "not-mounted")));   // an absent root is never created
    }

    [Fact]
    public void Stray_R2_settings_are_ignored_and_the_root_is_still_required()
    {
        ConfigureR2();

        Assert.Contains(StartupFailure(Host("Development", "  ")), m => m.Contains("Storage:RootPath is not configured"));

        using WebApplicationFactory<Program> factory = Host("Development", _root);
        Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
    }

    [Fact]
    public void Legacy_module_roots_cannot_override_the_central_root()
    {
        string decoyForm16 = Path.Combine(_root, "decoy-form16"), decoySlips = Path.Combine(_root, "decoy-slips"), decoyFiles = Path.Combine(_root, "decoy-files");
        foreach (string decoy in new[] { decoyForm16, decoySlips, decoyFiles }) Directory.CreateDirectory(decoy);
        Environment.SetEnvironmentVariable("Form16Storage__RootPath", decoyForm16);
        Environment.SetEnvironmentVariable("SalarySlipStorage__RootPath", decoySlips);
        Environment.SetEnvironmentVariable("FileStorage__BasePath", decoyFiles);
        string central = Path.Combine(_root, "central");
        Directory.CreateDirectory(central);

        using WebApplicationFactory<Program> factory = Host("Development", central);
        IStorageRoot root = factory.Services.GetRequiredService<IStorageRoot>();

        foreach (StorageModule module in StorageModules.All)
            Assert.StartsWith(Path.GetFullPath(central) + Path.DirectorySeparatorChar, root.ModuleRoot(module));
        Assert.Empty(Directory.GetFileSystemEntries(decoyForm16));
        Assert.Empty(Directory.GetFileSystemEntries(decoySlips));
        Assert.Empty(Directory.GetFileSystemEntries(decoyFiles));
    }

    [Fact]
    public void Staging_uses_the_central_root_for_every_module_and_needs_it()
    {
        using (WebApplicationFactory<Program> factory = Host("Staging", _root))
        {
            Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
            Assert.True(factory.Services.GetRequiredService<IStorageRoot>().IsConfigured);
        }

        Assert.Contains(StartupFailure(Host("Staging", "")), m => m.Contains("Storage:RootPath is not configured"));
        Assert.Contains(StartupFailure(Host("Staging", "  ")), m => m.Contains("Storage:RootPath is not configured"));
        Assert.Contains(StartupFailure(Host("Staging", "relative")), m => m.Contains("Storage:RootPath"));
    }

    // ── Production: local storage only, with or without stray R2 settings ──

    [Fact]
    public void Production_with_a_valid_root_uses_local_storage_for_every_module()
    {
        using WebApplicationFactory<Program> factory = Host("Production", _root);

        Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
        Assert.IsType<Form16StorageService>(Get<IForm16StorageService>(factory));
        Assert.True(factory.Services.GetRequiredService<IStorageRoot>().IsConfigured);
        Assert.True(factory.Services.GetRequiredService<IStorageAvailability>().IsAvailable);
        Assert.True(Directory.Exists(Path.Combine(_root, "Grievance")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Banners")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("relative-storage")]
    public void Production_without_a_usable_root_does_not_start(string root)
    {
        Assert.Contains(StartupFailure(Host("Production", root)), m => m.Contains("Storage:RootPath"));
    }

    [Fact]
    public void Production_with_complete_R2_settings_ignores_them_and_still_requires_the_root()
    {
        ConfigureR2();
        Environment.SetEnvironmentVariable("FileStorage__BasePath", "/var/data/files");   // a removed legacy setting: ignored

        Assert.Contains(StartupFailure(Host("Production", "")), m => m.Contains("Storage:RootPath is not configured"));

        using WebApplicationFactory<Program> factory = Host("Production", _root);
        Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
    }

    [Fact]
    public void A_half_configured_R2_section_is_ignored_and_never_blocks_startup()
    {
        Environment.SetEnvironmentVariable("R2Storage__ServiceUrl", "https://acct.r2.cloudflarestorage.com");   // bucket and credentials missing

        using WebApplicationFactory<Program> factory = Host("Staging", _root);

        Assert.IsType<LocalFileStorageService>(Get<IFileStorageService>(factory));
    }

    [Fact]
    public async Task Stored_files_round_trip_through_the_registered_provider_under_the_root()
    {
        using WebApplicationFactory<Program> factory = Host("Production", _root);
        using IServiceScope scope = factory.Services.CreateScope();
        IFileStorageService storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        string key;
        using (MemoryStream content = new(new byte[] { 1, 2, 3 }))
            key = await storage.StoreAsync(content, "x.pdf", "application/pdf", "grievances");

        Assert.Matches(@"^grievances/\d{4}/\d{2}/[0-9a-f-]{36}\.pdf$", key);
        FileStorageResult? file = await storage.GetAsync(key);
        Assert.NotNull(file);
        await using (file!.Stream) Assert.Equal(3, file.Stream.Length);
        Assert.True(await storage.DeleteAsync(key));
        Assert.Null(await storage.GetAsync(key));
    }
}
