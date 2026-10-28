using System.Text.RegularExpressions;

namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>Masks sensitive identifiers for display: only the last four characters stay visible.</summary>
public static class PisMasking
{
    private const int VisibleCharacters = 4;

    // Shape of a mask for the longest/shortest real identifiers (PAN 10, account 9-18, Aadhaar/PRAN 12 characters).
    private static readonly Regex MaskShape = new(@"^X{5,14}[0-9A-Z]{4}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Mask(string plain)
        => plain.Length <= VisibleCharacters
            ? new string('X', plain.Length)
            : new string('X', plain.Length - VisibleCharacters) + plain[^VisibleCharacters..];

    /// <summary>
    /// True when the (already normalised) value has the shape of a mask. Shape alone proves nothing: it only lets a
    /// request through validation so the service can compare it with the mask of the stored value.
    /// </summary>
    public static bool LooksMasked(string? normalised) => normalised != null && MaskShape.IsMatch(normalised);

    /// <summary>True only when the value is exactly the mask of <paramref name="storedPlain"/>.</summary>
    public static bool IsMaskOf(string normalised, string storedPlain)
        => string.Equals(normalised, Mask(storedPlain), StringComparison.Ordinal);
}
