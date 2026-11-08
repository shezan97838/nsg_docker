using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Tests;

/// <summary>Builds <see cref="StorageRoot"/> instances for tests.</summary>
internal static class TestStorage
{
    private static readonly string ProcessRoot = Path.Combine(Path.GetTempPath(), "nsg-tests-storage-" + Environment.ProcessId);

    /// <summary>
    /// <c>Storage:RootPath</c> is mandatory, and the development machine's appsettings.Development.json points it at <c>D:\NSGStorage</c>, which holds real
    /// data and may not exist elsewhere. Every host started by the suite therefore gets a throw-away root through the environment (which outranks the
    /// JSON file) unless a test sets its own. The suite never touches the development storage.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepHostsOffTheDevelopmentStorageRoot()
    {
        Directory.CreateDirectory(ProcessRoot);
        Environment.SetEnvironmentVariable("Storage__RootPath", ProcessRoot);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(ProcessRoot)) Directory.Delete(ProcessRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
    }

    /// <summary>A root at <paramref name="root"/> (the folder is created if missing).</summary>
    public static StorageRoot Central(string root)
    {
        Directory.CreateDirectory(root);
        return CentralUnchecked(root);
    }

    /// <summary>The same options without creating anything, for tests of the validation.</summary>
    public static StorageRoot CentralUnchecked(string? root) => new(Options.Create(new StorageOptions { RootPath = root ?? string.Empty }));

    /// <summary>Physical file of a stored salary slip path under <paramref name="root"/>.</summary>
    public static string SlipPath(string root, string storedPath)
        => Path.Combine(root, "SalarySlips", storedPath["salary-slips/".Length..].Replace('/', Path.DirectorySeparatorChar));
}
