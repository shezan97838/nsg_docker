using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.Import;

/// <summary>Conflict/failure type codes reported per row.</summary>
public static class PisImportConflictTypes
{
    public const string RequiredFieldMissing = "REQUIRED_FIELD_MISSING";
    public const string InvalidDate = "INVALID_DATE";
    public const string InvalidValue = "INVALID_VALUE";
    public const string NsgNumberNotFound = "NSG_NUMBER_NOT_FOUND";
    public const string DuplicateNsgNumberInFile = "DUPLICATE_NSG_NUMBER_IN_FILE";
    public const string DuplicateNsgNumber = "DUPLICATE_NSG_NUMBER";
    public const string DuplicateMobileNumber = "DUPLICATE_MOBILE_NUMBER";
    public const string DuplicateMobileNumberInFile = "DUPLICATE_MOBILE_NUMBER_IN_FILE";
    public const string SaveFailed = "SAVE_FAILED";

    /// <summary>The record is assigned to another PIS Admin, so this admin may not update it.</summary>
    public const string AssignedToAnotherAdmin = "RECORD_ASSIGNED_TO_ANOTHER_ADMIN";

    /// <summary>The record has no responsible PIS Admin yet (ownership is assigned through the Super Admin transfer workflow, never by an upload).</summary>
    public const string RecordUnassigned = "RECORD_UNASSIGNED";
}

/// <summary>One problem found on a row. <see cref="Value"/> is always null for the four sensitive identifiers.</summary>
public sealed record PisRowIssue(
    PisRowStatus Status, string ConflictType, string? Field, string? Value, string Message, string? ConflictingNsgNumber = null);

/// <summary>A worksheet row after parsing: plaintext non-sensitive values on <see cref="Values"/>, sensitive plaintext separately.</summary>
public sealed class PisParsedRow
{
    public required int RowNumber { get; init; }
    public required UserPis Values { get; init; }
    public required PisSensitiveIdentifiersInput Sensitive { get; init; }
    public List<PisRowIssue> Issues { get; } = new();

    /// <summary>Trimmed NSG number as typed; null when blank.</summary>
    public string? NsgNumber => string.IsNullOrWhiteSpace(Values.NsgNumber) ? null : Values.NsgNumber;
}

/// <summary>
/// Converts one worksheet row into a <see cref="PisParsedRow"/> and validates it with the same FluentValidation rules the PIS API uses.
/// Text is trimmed and kept as typed (blank = null); dates must be real Excel dates or strict DD/MM/YYYY text.
/// </summary>
public static class PisRowParser
{
    private const int MaxIssuesPerRow = 50;
    private static readonly Regex DatePattern = new(@"^\d{2}/\d{2}/\d{4}$", RegexOptions.CultureInvariant);
    private static readonly UserPisValidator Validator = new();
    private static readonly PisSensitiveIdentifiersValidator SensitiveValidator = new();
    private static readonly HashSet<string> SensitiveNames = new(
        PisImportFields.All.Where(f => f.Kind == PisFieldKind.Sensitive).Select(f => f.Name));

    public static PisParsedRow Parse(PisExcelRow raw)
    {
        PisParsedRow row = new() { RowNumber = raw.RowNumber, Values = new UserPis(), Sensitive = new PisSensitiveIdentifiersInput() };
        UserPis p = row.Values;

        for (int i = 0; i < PisImportFields.All.Count; i++)
        {
            PisImportField field = PisImportFields.All[i];
            PisExcelCell? cell = raw.Cells[i];
            string? text = cell?.Text;

            if (field.Kind == PisFieldKind.Date)
            {
                DateOnly? date = ParseDate(cell, row, field);
                switch (field.Name)
                {
                    case "DateOfInductionInNsg": p.DateOfInductionInNsg = date; break;
                    case "DateOfRepatriation": p.DateOfRepatriation = date; break;
                    case "DateOfBirth": p.DateOfBirth = date; break;
                    case "DateOfEnrollmentInGovtService": p.DateOfEnrollmentInGovtService = date; break;
                    case "DateOfSuperannuation": p.DateOfSuperannuation = date; break;
                    case "DateOfAme": p.DateOfAme = date; break;
                    case "DateOfImmovablePropertyReturn": p.DateOfImmovablePropertyReturn = date; break;
                }

                continue;
            }

            switch (field.Name)
            {
                case "NsgNumber": p.NsgNumber = text ?? string.Empty; break;
                case "ServiceNumber": p.ServiceNumber = text; break;
                case "NsgRank": p.NsgRank = text; break;
                case "ParentDepartmentRank": p.ParentDepartmentRank = text; break;
                case "FullName": p.FullName = text; break;
                case "NsgUnit": p.NsgUnit = text; break;
                case "Education": p.Education = text; break;
                case "SideSteppedDetails": p.SideSteppedDetails = text; break;
                case "Gender": p.Gender = text; break;
                case "Category": p.Category = text; break;
                case "Religion": p.Religion = text; break;
                case "AadhaarNumber": row.Sensitive.AadhaarNumber = text; break;
                case "PanNumber": row.Sensitive.PanNumber = text; break;
                case "PranNumber": row.Sensitive.PranNumber = text; break;
                case "SalaryAccountNumber": row.Sensitive.SalaryAccountNumber = text; break;
                case "BloodGroup": p.BloodGroup = text; break;
                case "HeightCm": p.HeightCm = text; break;
                case "WeightKg": p.WeightKg = text; break;
                case "MobileNumber": p.MobileNumber = text ?? string.Empty; break;
                case "EarnedLeaveDue": p.EarnedLeaveDue = text; break;
                case "HalfPayLeaveDue": p.HalfPayLeaveDue = text; break;
                case "PaternityLeaveAvailed": p.PaternityLeaveAvailed = text; break;
                case "MaternityLeaveAvailed": p.MaternityLeaveAvailed = text; break;
                case "ChildCareLeaveAvailed": p.ChildCareLeaveAvailed = text; break;
                case "FatherName": p.FatherName = text; break;
                case "MotherName": p.MotherName = text; break;
                case "SpouseName": p.SpouseName = text; break;
                case "NextOfKinMobileNumber": p.NextOfKinMobileNumber = text; break;
                case "NumberOfChildren": p.NumberOfChildren = text; break;
                case "PermanentVillage": p.PermanentVillage = text; break;
                case "PermanentPostOffice": p.PermanentPostOffice = text; break;
                case "PermanentPoliceStation": p.PermanentPoliceStation = text; break;
                case "PermanentDistrict": p.PermanentDistrict = text; break;
                case "PermanentState": p.PermanentState = text; break;
                case "PermanentPinCode": p.PermanentPinCode = text; break;
            }
        }

        Validate(row);
        if (row.Issues.Count > MaxIssuesPerRow) row.Issues.RemoveRange(MaxIssuesPerRow, row.Issues.Count - MaxIssuesPerRow);
        return row;
    }

    private static DateOnly? ParseDate(PisExcelCell? cell, PisParsedRow row, PisImportField field)
    {
        if (cell == null) return null;
        if (cell.Date.HasValue) return InRange(cell.Date.Value) ? cell.Date : Invalid(cell.Text);

        string? text = cell.Text;
        if (text == null) return null;
        if (DatePattern.IsMatch(text)
            && DateOnly.TryParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            && InRange(parsed))
            return parsed;

        return Invalid(text);

        DateOnly? Invalid(string? value)
        {
            row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.InvalidDate, field.ReportName,
                Truncate(value), "Invalid date. Expected DD/MM/YYYY."));
            return null;
        }
    }

    private static bool InRange(DateOnly d) => d.Year is >= 1900 and <= 2100;

    private static void Validate(PisParsedRow row)
    {
        HashSet<string> reported = new(row.Issues.Select(i => i.Field ?? string.Empty));

        void Add(IEnumerable<ValidationFailure> failures)
        {
            foreach (IGrouping<string, ValidationFailure> group in failures.GroupBy(f => f.PropertyName))
            {
                // A blank mobile number is judged by the import service (a blank is fine on update when the record already has one).
                if (group.Key == nameof(UserPis.MobileNumber) && string.IsNullOrEmpty(row.Values.MobileNumber)) continue;

                ValidationFailure first = group.First();
                string field = char.ToLowerInvariant(first.PropertyName[0]) + first.PropertyName[1..];
                if (!reported.Add(field)) continue;

                bool sensitive = SensitiveNames.Contains(first.PropertyName);
                string type = first.ErrorCode == "NotEmptyValidator" ? PisImportConflictTypes.RequiredFieldMissing : PisImportConflictTypes.InvalidValue;
                string? value = sensitive ? null : Truncate(first.AttemptedValue?.ToString());
                row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, type, field, string.IsNullOrEmpty(value) ? null : value, first.ErrorMessage));
            }
        }

        Add(Validator.Validate(row.Values).Errors);
        Add(SensitiveValidator.Validate(row.Sensitive).Errors);
    }

    private static string? Truncate(string? value)
        => value == null ? null : value.Length <= 100 ? value : value[..100] + "…";
}
