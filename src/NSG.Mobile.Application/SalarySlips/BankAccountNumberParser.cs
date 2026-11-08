using System.Text.RegularExpressions;

namespace NSG.Mobile.Application.SalarySlips;

/// <summary>
/// Reads the bank account number from the text of one salary slip page. The value is taken ONLY from the <c>Bank A/c No</c>
/// label (for example <c>Bank A/c No : 34280226082</c>); other numbers on the page, including <c>PRAN No</c>, are never considered.
/// </summary>
public static class BankAccountNumberParser
{
    public const int MinDigits = 6;
    public const int MaxDigits = 25;

    // "Bank A/c No", tolerant of spacing, case and a trailing dot ("Bank A/C No."), followed by an optional ":" or "-" and the digits.
    private static readonly Regex Pattern = new(
        @"Bank\s*A\s*/\s*C\.?\s*No\.?\s*[:\-]?\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    public static (string? AccountNumber, SalarySlipPageIssue Issue) Parse(string? pageText)
    {
        if (string.IsNullOrWhiteSpace(pageText))
            return (null, SalarySlipPageIssue.NoText);

        HashSet<string> values = new(StringComparer.Ordinal);
        try
        {
            foreach (Match match in Pattern.Matches(pageText))
            {
                string digits = match.Groups[1].Value;
                if (digits.Length is >= MinDigits and <= MaxDigits)
                    values.Add(digits);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, SalarySlipPageIssue.AccountNumberNotFound);
        }

        return values.Count switch
        {
            0 => (null, SalarySlipPageIssue.AccountNumberNotFound),
            1 => (values.First(), SalarySlipPageIssue.None),
            _ => (null, SalarySlipPageIssue.AccountNumberAmbiguous)
        };
    }
}
