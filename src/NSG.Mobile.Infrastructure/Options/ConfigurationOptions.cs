using System.ComponentModel.DataAnnotations;

namespace NSG.Mobile.Infrastructure.Options;

/// <summary>JWT token configuration. Secrets must come from secure config — never source code.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Secret { get; set; } = string.Empty;
    [Required] public string Issuer { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// Audience of Admin Portal access tokens. Optional: when empty it is <c>{Audience}.Admin</c>. It must differ from
    /// <see cref="Audience"/>, which is what keeps admin and mobile tokens from being accepted in each other's place.
    /// </summary>
    public string? AdminAudience { get; set; }

    public string EffectiveAdminAudience => string.IsNullOrWhiteSpace(AdminAudience) ? Audience + ".Admin" : AdminAudience;
}

/// <summary>Security configuration for OTP, lockout, and password policies.</summary>
public class SecurityOptions
{
    public const string SectionName = "Security";

    public int OtpExpirationSeconds { get; set; } = 300;
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpResendCooldownSeconds { get; set; } = 60;
    public int OtpLength { get; set; } = 6;
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 30;
    public int PasswordMinLength { get; set; } = 8;
}

/// <summary>Redis distributed cache configuration.</summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    [Required] public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "NSG:";
}

/// <summary>NIC SMS gateway configuration. Credentials from secure config only.</summary>
public class SmsOptions
{
    public const string SectionName = "Sms";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    // TODO: Actual NIC SMS gateway fields TBD
}

/// <summary>FCM push notification configuration.</summary>
public class FcmOptions
{
    public const string SectionName = "Fcm";

    public string ProjectId { get; set; } = string.Empty;
    public string CredentialPath { get; set; } = string.Empty;
}

/// <summary>HRMS integration configuration.</summary>
public class HrmsOptions
{
    public const string SectionName = "Hrms";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Payroll integration configuration.</summary>
public class PayrollOptions
{
    public const string SectionName = "Payroll";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Secure file storage configuration.</summary>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    [Required] public string BasePath { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB default
    public string[] AllowedExtensions { get; set; } = { ".pdf", ".jpg", ".jpeg", ".png", ".xml" };
}

/// <summary>Encryption key configuration. Keys must come from secure config — NEVER source code.</summary>
public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    [Required] public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Key for the deterministic blind index (HMAC-SHA256) over PAN and salary account number.
/// Base64, at least 32 bytes, and DIFFERENT from the encryption key. Secure config only — NEVER source code.
/// </summary>
public class BlindIndexOptions
{
    public const string SectionName = "BlindIndex";

    [Required] public string Key { get; set; } = string.Empty;
}

/// <summary>How OTPs are produced and delivered. <see cref="OtpMode.Sms"/> (the default) is the only mode allowed in Production.</summary>
public enum OtpMode
{
    /// <summary>Random OTP delivered through <c>INicSmsService</c>. If the gateway is missing or fails, the send fails: there is no fallback.</summary>
    Sms = 0,

    /// <summary>TEMPORARY presentation mode: every OTP is the fixed code <see cref="OtpOptions.DemoCode"/> and nothing is sent.</summary>
    Demo = 1
}

/// <summary>
/// OTP mode configuration (<c>Otp</c> section). Demo mode is refused at startup unless the environment is Development, or Staging/Demo
/// with <see cref="AllowDemoOutsideDevelopment"/> set explicitly; it is never accepted in Production (see <c>OtpModeGuard</c>).
/// </summary>
public class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>The fixed OTP used in demo mode. Not a secret; the mode is what is guarded.</summary>
    public const string DemoCode = "123456";

    public OtpMode Mode { get; set; } = OtpMode.Sms;

    /// <summary>Explicit approval to run demo mode in a Staging/Demo environment (for an approved presentation). Ignored in Production.</summary>
    public bool AllowDemoOutsideDevelopment { get; set; }
}
