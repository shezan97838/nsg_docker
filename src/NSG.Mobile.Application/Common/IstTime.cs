namespace NSG.Mobile.Application.Common;

/// <summary>
/// Converts stored IST instants to DateTimeOffset with +05:30 offset at the API boundary. IST is a fixed UTC+05:30 offset with no daylight
/// saving, so a constant offset is exact and does not depend on the host's time-zone database (IANA on Linux, registry on Windows).
/// </summary>
public static class IstTime
{
    public static readonly TimeSpan Offset = new(5, 30, 0);

    /// <summary>Represents an IST instant as a DateTimeOffset with the +05:30 offset.</summary>
    public static DateTimeOffset FromIst(DateTime ist)
        => new(DateTime.SpecifyKind(ist, DateTimeKind.Unspecified), Offset);

    /// <summary>The same instant expressed with the +05:30 offset. <paramref name="utc"/> is treated as UTC whatever its Kind.</summary>
    public static DateTimeOffset FromUtc(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Offset);
}
