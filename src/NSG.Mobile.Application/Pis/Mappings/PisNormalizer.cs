namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>Canonical forms applied to PIS input before validation and persistence.</summary>
public static class PisNormalizer
{
    /// <summary>Trims an identifier such as the NSG number or service number; case and punctuation are preserved.</summary>
    public static string Identifier(string value) => value.Trim();

    /// <summary>Salary account number is trimmed; null or blank becomes null. Digits-only is enforced by validation.</summary>
    public static string? SalaryAccountNumber(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>PAN is trimmed and upper-cased; null or blank becomes null.</summary>
    public static string? Pan(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
