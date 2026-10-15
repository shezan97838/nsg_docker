using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Application.Common;

/// <summary>
/// Utility for normalizing Indian mobile numbers to a consistent format.
/// SOW §3.7: Mobile number is the login identifier.
/// </summary>
public static class MobileNumberNormalizer
{
    /// <summary>
    /// Normalize a mobile number to +91XXXXXXXXXX format.
    /// Strips spaces, dashes, and handles common input variations.
    /// </summary>
    public static string Normalize(string mobileNumber)
    {
        if (string.IsNullOrWhiteSpace(mobileNumber))
            return string.Empty;

        // Remove spaces, dashes, parentheses
        var cleaned = new string(mobileNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());

        // Handle +91 prefix
        if (cleaned.StartsWith("+91") && cleaned.Length == 13)
            return cleaned;

        // Handle 91 prefix (without +)
        if (cleaned.StartsWith("91") && cleaned.Length == 12)
            return "+" + cleaned;

        // Handle 0 prefix
        if (cleaned.StartsWith("0") && cleaned.Length == 11)
            return DomainConstants.MobileNumber.IndiaCountryCode + cleaned[1..];

        // Handle raw 10-digit number
        if (cleaned.Length == DomainConstants.MobileNumber.ExpectedDigitCount)
            return DomainConstants.MobileNumber.IndiaCountryCode + cleaned;

        // Return as-is if it doesn't match expected patterns (validation will catch it)
        return cleaned;
    }
}
