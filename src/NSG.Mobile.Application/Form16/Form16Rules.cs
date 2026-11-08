using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NSG.Mobile.Application.Form16;

/// <summary>Which of the two Form 16 documents.</summary>
public enum Form16Part
{
    PartA = 1,
    PartB = 2
}

/// <summary>Validation rules shared by the Form 16 services and storage. The assessment year is whitelisted; the NSG number is encoded into a folder name.</summary>
public static partial class Form16Rules
{
    /// <summary>File name of each part inside <c>{assessmentYear}/{nsgNumber}/</c>.</summary>
    public static string FileName(Form16Part part) => part == Form16Part.PartA ? "part-a.pdf" : "part-b.pdf";

    [GeneratedRegex(@"^(\d{4})-(\d{2})$")]
    private static partial Regex AssessmentYearPattern();

    /// <summary>Longest NSG number PIS stores (<c>users_pis.nsg_number</c> is varchar(30)).</summary>
    public const int NsgNumberMaxLength = 30;

    // Device names Windows refuses as a file or folder name, with or without an extension (development machines only; harmless elsewhere).
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Accepts <c>yyyy-yy</c> where the second part is the following year (<c>2026-27</c>, <c>2099-00</c>). Returns the trimmed value or null.
    /// </summary>
    public static string? NormalizeAssessmentYear(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        Match match = AssessmentYearPattern().Match(trimmed);
        if (!match.Success) return null;

        int start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int end = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return start is >= 2000 and <= 2099 && end == (start + 1) % 100 ? trimmed : null;
    }

    /// <summary>
    /// The same envelope PIS accepts for an NSG number: not blank, at most 30 characters, no control characters. There is no approved NSG number
    /// format (OQ-032), so nothing stricter is imposed here; whether the person exists is decided by the PIS lookup, and the value never reaches the
    /// filesystem except through <see cref="FolderNameFor"/>.
    /// </summary>
    public static bool IsValidNsgNumber(string? nsgNumber)
        => !string.IsNullOrWhiteSpace(nsgNumber) && nsgNumber.Length <= NsgNumberMaxLength && !nsgNumber.Any(char.IsControl);

    /// <summary>
    /// The folder name for an NSG number. Letters, digits, '-' and '_' are kept; every other character (including '.', '/', '\', ':', spaces and
    /// '%') becomes <c>%XX</c> per UTF-8 byte. The mapping is reversible, so two NSG numbers never share a folder, and the result can never be
    /// a path separator, a dot segment ("." / ".."), a name with a trailing dot or space, or (after the escape below) a Windows device name.
    /// </summary>
    public static string FolderNameFor(string nsgNumber)
    {
        StringBuilder name = new(nsgNumber.Length);
        foreach (char c in nsgNumber)
        {
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
                name.Append(c);
            else
                foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                    name.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        string result = name.ToString();
        // "CON" as a folder name is a device on Windows; escape its first letter so it stays distinct and usable.
        return WindowsReservedNames.Contains(result)
            ? "%" + ((int)result[0]).ToString("X2", CultureInfo.InvariantCulture) + result[1..]
            : result;
    }
}
