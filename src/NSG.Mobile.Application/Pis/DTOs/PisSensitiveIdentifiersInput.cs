namespace NSG.Mobile.Application.Pis.DTOs;

/// <summary>
/// Plaintext sensitive identifiers as received from a caller, before they are normalised, validated and encrypted
/// into the <c>*Encrypted</c> columns of <c>UserPis</c>. Never logged, serialised into responses or stored as-is.
/// </summary>
public class PisSensitiveIdentifiersInput
{
    /// <summary>Aadhaar number: exactly 12 digits.</summary>
    public string? AadhaarNumber { get; set; }

    /// <summary>PAN: format AAAAA9999A; case-insensitive on input, stored upper-case.</summary>
    public string? PanNumber { get; set; }

    /// <summary>PRAN (provisional format, see OQ-024).</summary>
    public string? PranNumber { get; set; }

    /// <summary>Salary account number: digits only, not a fixed length (provisional range, see OQ-024).</summary>
    public string? SalaryAccountNumber { get; set; }
}
