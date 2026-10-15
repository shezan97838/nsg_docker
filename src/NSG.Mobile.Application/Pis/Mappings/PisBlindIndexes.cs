using NSG.Mobile.Application.Interfaces.Infrastructure;

namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>
/// Computes the uniqueness hashes stored beside the encrypted PAN and salary account number. Call after validation and
/// before saving; always pass the raw caller input and let this class apply the canonical form.
/// </summary>
public static class PisBlindIndexes
{
    // These two labels are part of every stored hash (HMAC domain separation). They only LOOK like database names: they are NOT
    // identifiers, must never follow table or column renames, and changing them invalidates all stored hashes.
    private const string PanPurpose = "UsersPIS.Pan";
    private const string SalaryAccountPurpose = "UsersPIS.SalaryAccountNumber";

    /// <summary>Hash of the trimmed, upper-cased PAN; null for a missing PAN.</summary>
    public static string? ForPan(this IBlindIndexService service, string? pan)
    {
        string? normalized = PisNormalizer.Pan(pan);
        return normalized is null ? null : service.Compute(PanPurpose, normalized);
    }

    /// <summary>Hash of the trimmed salary account number (digits only once validated); null when missing.</summary>
    public static string? ForSalaryAccountNumber(this IBlindIndexService service, string? accountNumber)
    {
        string? normalized = PisNormalizer.SalaryAccountNumber(accountNumber);
        return normalized is null ? null : service.Compute(SalaryAccountPurpose, normalized);
    }
}
