namespace NSG.Mobile.Application.Pis.Mappings;

/// <summary>Masks sensitive identifiers for display: only the last four characters stay visible.</summary>
public static class PisMasking
{
    private const int VisibleCharacters = 4;

    public static string Mask(string plain)
        => plain.Length <= VisibleCharacters
            ? new string('X', plain.Length)
            : new string('X', plain.Length - VisibleCharacters) + plain[^VisibleCharacters..];
}
