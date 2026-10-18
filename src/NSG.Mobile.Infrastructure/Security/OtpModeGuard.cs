using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// OTP mode rules. By client/owner decision (2026-10-18) the fixed demo OTP (123456, no SMS) is allowed in every environment,
/// including Production, and on any database. The former fail-closed restriction (Development / approved Staging only, and a
/// <c>_demo</c> database name) has been removed on purpose; the methods are kept so callers and the OtpService constructor stay unchanged.
/// REVERT before the BSNL SMS gateway goes live: set <c>Otp:Mode=Sms</c> and restore the restrictions.
/// </summary>
public static class OtpModeGuard
{
    /// <summary>Demo mode is accepted in every environment (see the class remarks).</summary>
    public static void EnsureAllowed(OtpOptions options, string? environmentName)
    {
    }

    /// <summary>Demo mode no longer requires a database name ending in <c>_demo</c> (see the class remarks).</summary>
    public static void EnsureDemoDatabase(OtpOptions options, string? environmentName, string? connectionString)
    {
    }
}
