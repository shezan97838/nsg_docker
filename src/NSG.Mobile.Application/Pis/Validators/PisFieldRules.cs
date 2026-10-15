using System.Text.RegularExpressions;
using FluentValidation;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Application.Pis.Validators;

/// <summary>
/// Reusable FluentValidation rules for PIS fields. Messages never include the offending value, because several of
/// these fields are sensitive identifiers.
/// </summary>
public static class PisFieldRules
{
    // A trailing line feed would satisfy '$' in .NET, so it is rejected explicitly.
    private static bool IsExact(string value, string pattern)
        => value.IndexOf('\n') < 0 && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);

    /// <summary>Optional value; when present, exactly ten digits starting 6-9.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeIndianMobile<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(v, DomainConstants.PisFormats.MobileNumberPattern))
            .WithMessage("{PropertyName} must be a 10-digit Indian mobile number.");

    /// <summary>Optional value; when present, a PAN (AAAAA9999A) after trimming and upper-casing.</summary>
    public static IRuleBuilderOptions<T, string?> MustBePan<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(PisNormalizer.Pan(v) ?? string.Empty, DomainConstants.PisFormats.PanPattern))
            .WithMessage("{PropertyName} must be in the format AAAAA9999A.");

    /// <summary>Optional value; when present, exactly twelve digits.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeAadhaar<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(v, DomainConstants.PisFormats.AadhaarPattern))
            .WithMessage("{PropertyName} must be exactly 12 digits.");

    /// <summary>Optional value; when present, a PRAN in the provisional format.</summary>
    public static IRuleBuilderOptions<T, string?> MustBePran<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(v, DomainConstants.PisFormats.PranPattern))
            .WithMessage("{PropertyName} must be exactly 12 digits.");

    /// <summary>Optional value; when present, digits only with a length of 9 to 18.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeSalaryAccountNumber<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(v, DomainConstants.PisFormats.SalaryAccountPattern))
            .WithMessage("{PropertyName} must contain 9 to 18 digits only.");

    /// <summary>Optional value; when present, a six-digit PIN code that does not start with 0.</summary>
    public static IRuleBuilderOptions<T, string?> MustBePinCode<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(v => v is null || IsExact(v, DomainConstants.PisFormats.PinCodePattern))
            .WithMessage("{PropertyName} must be a 6-digit PIN code.");

    /// <summary>Required identifier that must already be trimmed (the caller normalises first).</summary>
    public static IRuleBuilderOptions<T, string> MustBeRequiredTrimmedIdentifier<T>(this IRuleBuilder<T, string> rule, int maxLength)
        => rule.NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(maxLength).WithMessage("{PropertyName} must not exceed " + maxLength + " characters.")
            .Must(v => v == v.Trim()).WithMessage("{PropertyName} must not have leading or trailing spaces.");
}
