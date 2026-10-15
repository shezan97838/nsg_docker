namespace NSG.Mobile.Domain.Common;

/// <summary>
/// Cross-platform helper for Indian Standard Time (IST / UTC+05:30).
/// Resolves IANA "Asia/Kolkata" on Linux/Docker and "India Standard Time" on Windows,
/// with a fixed +05:30 fallback without Daylight Saving Time.
/// </summary>
public static class IndianStandardTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // 1. Try IANA standard identifier (Linux / macOS / Docker)
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Kolkata", out TimeZoneInfo? iana))
            return iana;

        // 2. Try Windows registry identifier (Windows Server / Local Dev)
        if (TimeZoneInfo.TryFindSystemTimeZoneById("India Standard Time", out TimeZoneInfo? win))
            return win;

        // 3. Fallback: Custom fixed-offset time zone (+05:30)
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "India Standard Time");
    }

    /// <summary>Returns the current instant in Indian Standard Time.</summary>
    public static DateTime Now => ConvertFromUtc(DateTime.UtcNow);

    /// <summary>Converts a UTC instant to Indian Standard Time.</summary>
    public static DateTime ConvertFromUtc(DateTime utcInstant) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), Zone);

    /// <summary>Converts an Indian Standard Time instant to UTC.</summary>
    public static DateTime ConvertToUtc(DateTime istInstant) =>
        TimeZoneInfo.ConvertTimeToUtc(istInstant, Zone);
}
