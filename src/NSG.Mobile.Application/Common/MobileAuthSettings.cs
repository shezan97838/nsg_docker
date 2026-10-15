namespace NSG.Mobile.Application.Common;

/// <summary>
/// Tunables for the mobile registration and login workflow. Built once at startup by the composition root from the
/// <c>Security</c> and <c>MobileAuth</c> configuration sections (Options pattern); services never read configuration directly.
/// </summary>
public class MobileAuthSettings
{
    /// <summary>Consecutive wrong MPINs before the profile is locked.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts, in minutes.</summary>
    public int LockoutMinutes { get; set; } = 30;

    /// <summary>Lifetime of a registration / MPIN-setup / MPIN-reset grant, in minutes.</summary>
    public int GrantLifetimeMinutes { get; set; } = 10;

    /// <summary>OTPs that may be sent to one mobile number for one flow in a rolling hour (on top of the resend cooldown).</summary>
    public int OtpMaxSendsPerHour { get; set; } = 5;

    /// <summary>Lifetime of a mobile refresh token, in days (from <c>Jwt:RefreshTokenExpirationDays</c>).</summary>
    public int RefreshTokenDays { get; set; } = 7;
}
