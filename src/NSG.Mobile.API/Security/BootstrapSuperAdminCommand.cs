using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.API.Security;

/// <summary>
/// <c>dotnet NSG.Mobile.API.dll bootstrap-super-admin --nsg-number &lt;number&gt; --confirm</c>: creates the first Super Admin as a
/// PendingActivation account with no password (ADR-017). The person then activates it through the Admin Portal API with an OTP
/// sent to the mobile number registered in PIS and chooses their own password. Nothing is printed or stored that could be used to
/// sign in. It uses the configured database, refuses to run once a Super Admin exists, and is never run by the web host itself.
/// </summary>
public static class BootstrapSuperAdminCommand
{
    public const string Name = "bootstrap-super-admin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        string? nsgNumber = ValueOf(args, "--nsg-number");
        if (string.IsNullOrWhiteSpace(nsgNumber) || !args.Contains("--confirm", StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Usage: {Name} --nsg-number <NSG number of the first Super Admin> --confirm");
            Console.Error.WriteLine("Creates a PendingActivation Super Admin without a password in the configured database. Review docs/architecture/ADR-017-admin-portal-authentication.md first.");
            return 2;
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        Result<long> result = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync(nsgNumber);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Not created: {result.ErrorMessage}");
            return 1;
        }

        Console.WriteLine($"Super Admin account {result.Data} created in PendingActivation. The person must now activate it (OTP to the PIS-registered mobile, then choose a password).");
        return 0;
    }

    private static string? ValueOf(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
