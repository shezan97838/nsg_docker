using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// Fail-closed rule for the fixed demo OTP. Demo mode is allowed only in Development, or in Staging/Demo when
/// <see cref="OtpOptions.AllowDemoOutsideDevelopment"/> is set. Production, an unset or unknown environment name, and anything
/// else are rejected, so a misconfigured deployment stops at startup instead of accepting 123456.
/// </summary>
public static class OtpModeGuard
{
    public static void EnsureAllowed(OtpOptions options, string? environmentName)
    {
        if (options.Mode != OtpMode.Demo) return;

        bool development = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        bool approvedPresentation = options.AllowDemoOutsideDevelopment
            && (string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase)
                || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase));

        if (!development && !approvedPresentation)
        {
            throw new InvalidOperationException(
                $"{OtpOptions.SectionName}:Mode=Demo is not allowed in the '{environmentName}' environment. " +
                "Use Mode=Sms, or run in Development (or Staging/Demo with Otp:AllowDemoOutsideDevelopment=true for an approved presentation).");
        }
    }

    /// <summary>
    /// A hosted demo (demo OTP outside Development) must run against a database that is dedicated to it: the database name has to
    /// end in <c>_demo</c>. This keeps a demo deployment from ever being pointed at a database that holds real data.
    /// </summary>
    public static void EnsureDemoDatabase(OtpOptions options, string? environmentName, string? connectionString)
    {
        if (options.Mode != OtpMode.Demo || string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)) return;

        string database = string.Empty;
        try { database = new MySqlConnector.MySqlConnectionStringBuilder(connectionString ?? string.Empty).Database; }
        catch (ArgumentException) { /* unparsable: treated as no database name below */ }

        if (!database.EndsWith("_demo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Otp:Mode=Demo outside Development requires a dedicated demo database: the database name in ConnectionStrings:MySql must end with '_demo'.");
        }
    }
}
