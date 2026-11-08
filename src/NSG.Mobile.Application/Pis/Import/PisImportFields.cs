using System.Text;

namespace NSG.Mobile.Application.Pis.Import;

/// <summary>How a worksheet column is read.</summary>
public enum PisFieldKind
{
    Text,
    Date,
    /// <summary>One of the four reversibly encrypted identifiers: read as text, never echoed back.</summary>
    Sensitive
}

/// <summary>
/// One of the 42 personnel columns of the finalized PIS workbook. <see cref="Name"/> equals the <c>UserPis</c> property name
/// (and, camel-cased, the field name used in upload reports).
/// </summary>
public sealed record PisImportField(
    string Name, string Heading, PisFieldKind Kind, bool IsAddress = false, params string[] Aliases)
{
    /// <summary>Field name as reported to clients (camelCase), for example <c>dateOfBirth</c>.</summary>
    public string ReportName { get; } = char.ToLowerInvariant(Name[0]) + Name[1..];
}

/// <summary>
/// The finalized workbook structure: row 1 = main headings, row 2 = serial numbers, row 3 = address sub-headings, data from row 4.
/// The permanent home address is a grouped heading with six sub-columns, read from row 3.
/// </summary>
public static class PisImportFields
{
    public const int HeaderRow = 1;
    public const int SubHeaderRow = 3;
    public const int FirstDataRow = 4;

    public static readonly IReadOnlyList<PisImportField> All = new PisImportField[]
    {
        new("NsgNumber", "NSG NO", PisFieldKind.Text),
        new("ServiceNumber", "IRLA/FORCE NO./ARMY NO.", PisFieldKind.Text),
        new("NsgRank", "RANK (NSG)", PisFieldKind.Text),
        new("ParentDepartmentRank", "RANK PARENT DEPARTMENT", PisFieldKind.Text),
        new("FullName", "NAME", PisFieldKind.Text),
        new("NsgUnit", "NSG UNIT", PisFieldKind.Text),
        new("Education", "EDUCATION", PisFieldKind.Text),
        new("DateOfInductionInNsg", "DATE OF INDUCTION IN NSG", PisFieldKind.Date),
        new("SideSteppedDetails", "DATE OF SIDE STEPPED & NSG UNIT", PisFieldKind.Text),
        new("DateOfRepatriation", "DATE OF REPATRIATION", PisFieldKind.Date),
        new("DateOfBirth", "DATE OF BIRTH", PisFieldKind.Date),
        new("DateOfEnrollmentInGovtService", "DATE OF ENROLLMENT/APPOINTMENT IN GOVT SERVICE", PisFieldKind.Date),
        new("DateOfSuperannuation", "DATE OF SUPERRANUATION", PisFieldKind.Date, false, "DATE OF SUPERANNUATION"),
        new("Gender", "Gender", PisFieldKind.Text),
        new("Category", "CATEGORY (SC/ST/GEN/OBC)", PisFieldKind.Text),
        new("Religion", "RELIGION", PisFieldKind.Text),
        new("AadhaarNumber", "AADHAR NO.", PisFieldKind.Sensitive, false, "AADHAAR NO."),
        new("PanNumber", "PAN NO", PisFieldKind.Sensitive, false, "PAN NO."),
        new("PranNumber", "PRAN NO.", PisFieldKind.Sensitive),
        new("BloodGroup", "BLOOD GROUP", PisFieldKind.Text),
        new("HeightCm", "HEIGHT (IN CM)", PisFieldKind.Text),
        new("WeightKg", "WEIGHT IN KGs", PisFieldKind.Text),
        new("MobileNumber", "MOBILE NO OF INDIVIDUAL", PisFieldKind.Text),
        new("DateOfImmovablePropertyReturn", "DATE OF IMMOVABLE PROPERTY RETURN", PisFieldKind.Date, false, "DATE OF IMMOVABLE PROPERTY RETURN (IPR)"),
        new("EarnedLeaveDue", "DETAILS OF EARNED/ANNUAL LEAVE DUE", PisFieldKind.Text),
        new("HalfPayLeaveDue", "DETAILS OF HALF PAY LEAVE DUE", PisFieldKind.Text),
        new("PaternityLeaveAvailed", "DETAILS OF PATERNITY LEAVE AVAILED", PisFieldKind.Text),
        new("MaternityLeaveAvailed", "DETAILS OF MATERNITY LEAVE AVAILED", PisFieldKind.Text),
        new("ChildCareLeaveAvailed", "DETAILS OF CHILD CARE LEAVE AVAILED", PisFieldKind.Text),
        new("DateOfAme", "DATE OF AME", PisFieldKind.Date),
        new("SalaryAccountNumber", "SALARY ACCOUNT NUMBER", PisFieldKind.Sensitive),
        new("FatherName", "NAME OF FATHER", PisFieldKind.Text),
        new("MotherName", "NAME OF MOTHER", PisFieldKind.Text),
        new("PermanentVillage", "ADDRESS – VILLAGE", PisFieldKind.Text, true, "VILLAGE"),
        new("PermanentPostOffice", "ADDRESS – POST", PisFieldKind.Text, true, "POST", "POST OFFICE"),
        new("PermanentPoliceStation", "ADDRESS – PS", PisFieldKind.Text, true, "PS", "POLICE STATION"),
        new("PermanentDistrict", "ADDRESS – DISTT", PisFieldKind.Text, true, "DISTT", "DISTRICT", "DIST"),
        new("PermanentState", "ADDRESS – STATE", PisFieldKind.Text, true, "STATE"),
        new("PermanentPinCode", "ADDRESS – PIN", PisFieldKind.Text, true, "PIN", "PIN CODE", "PINCODE"),
        new("SpouseName", "NAME OF SPOUSE", PisFieldKind.Text),
        new("NextOfKinMobileNumber", "MOBILE NO. OF NOK", PisFieldKind.Text),
        new("NumberOfChildren", "NO OF CHILDREN", PisFieldKind.Text),
    };

    /// <summary>The only optional fields; the other 35 are mandatory (a blank is acceptable on update only if the stored record already has a value).</summary>
    public static readonly IReadOnlySet<string> OptionalNames = new HashSet<string>
    {
        "SpouseName", "NumberOfChildren", "DateOfImmovablePropertyReturn", "HalfPayLeaveDue",
        "PaternityLeaveAvailed", "MaternityLeaveAvailed", "ChildCareLeaveAvailed"
    };

    public static bool IsMandatory(PisImportField field) => !OptionalNames.Contains(field.Name);

    public static int IndexOf(string name)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Name == name) return i;
        throw new ArgumentException($"Unknown PIS field '{name}'.", nameof(name));
    }

    /// <summary>
    /// Heading comparison key: upper-case, every run of non-alphanumeric characters (spaces, line breaks, dashes, slashes, dots)
    /// becomes one space. "DATE OF ENROLLMENT/APPOINTMENT IN GOVT SERVICE" and a line-wrapped copy of it compare equal.
    /// </summary>
    public static string Normalize(string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading)) return string.Empty;

        StringBuilder key = new(heading.Length);
        bool pendingSpace = false;
        foreach (char c in heading)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && key.Length > 0) key.Append(' ');
                pendingSpace = false;
                key.Append(char.ToUpperInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return key.ToString();
    }
}
