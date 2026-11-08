namespace NSG.Mobile.Tests;

/// <summary>Helpers for building personnel rows in tests: the individual's mobile number is mandatory and unique.</summary>
internal static class TestPisData
{
    private static long _next = 100000000;

    /// <summary>A valid, unique ten-digit mobile number (starts with 9).</summary>
    public static string Mobile() => "9" + Interlocked.Increment(ref _next).ToString("D9");
}
