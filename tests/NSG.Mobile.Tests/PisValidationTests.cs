using FluentValidation.Results;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Application.Pis.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Pure unit tests (no database) for the PIS field validation rules.</summary>
public class PisValidationTests
{
    private static readonly UserPisValidator Validator = new();
    private static readonly PisSensitiveIdentifiersValidator SensitiveValidator = new();

    private static UserPis Valid() => new() { NsgNumber = "123456", ServiceNumber = "123456789" };

    private static bool HasErrorOn(ValidationResult result, string property) => result.Errors.Any(e => e.PropertyName == property);

    // ── Identifiers ──

    [Fact]
    public void Minimal_record_with_only_the_two_mandatory_identifiers_is_valid()
        => Assert.True(Validator.Validate(Valid()).IsValid);

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" 123456", false)]   // must be trimmed by the caller
    [InlineData("123456 ", false)]
    [InlineData("0012-A/7", true)]   // leading zeros and punctuation are legitimate
    [InlineData("123456789012345678901234567890", true)]   // 30 characters
    [InlineData("1234567890123456789012345678901", false)] // 31 characters
    public void NsgNumber_and_ServiceNumber_are_required_trimmed_and_length_limited(string value, bool valid)
    {
        UserPis nsg = Valid(); nsg.NsgNumber = value;
        UserPis service = Valid(); service.ServiceNumber = value;

        Assert.Equal(valid, Validator.Validate(nsg).IsValid);
        Assert.Equal(valid, Validator.Validate(service).IsValid);
    }

    [Fact]
    public void Identifier_normaliser_trims_without_changing_case_or_punctuation()
        => Assert.Equal("ab-01/x", PisNormalizer.Identifier("  ab-01/x \t"));

    // ── Mobile numbers ──

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("6000000000", true)]
    [InlineData("5876543210", false)]   // must start 6-9
    [InlineData("987654321", false)]    // nine digits
    [InlineData("98765432101", false)]  // eleven digits
    [InlineData("+919876543210", false)] // no country code
    [InlineData("98765 43210", false)]
    [InlineData("98765abcde", false)]
    [InlineData("9876543210\n", false)]
    [InlineData("", false)]
    public void Mobile_numbers_must_be_exactly_ten_indian_digits(string value, bool valid)
    {
        UserPis own = Valid(); own.MobileNumber = value;
        UserPis nok = Valid(); nok.NextOfKinMobileNumber = value;

        Assert.Equal(valid, !HasErrorOn(Validator.Validate(own), nameof(UserPis.MobileNumber)));
        Assert.Equal(valid, !HasErrorOn(Validator.Validate(nok), nameof(UserPis.NextOfKinMobileNumber)));
    }

    [Fact]
    public void Missing_mobile_numbers_are_allowed()
    {
        UserPis p = Valid(); p.MobileNumber = null; p.NextOfKinMobileNumber = null;
        Assert.True(Validator.Validate(p).IsValid);
    }

    // ── Sensitive identifiers ──

    [Theory]
    [InlineData("AGIPR3625F", true)]
    [InlineData("agipr3625f", true)]    // normalised to upper-case
    [InlineData("  AGIPR3625F ", true)]
    [InlineData("AGIP3625FF", false)]
    [InlineData("AGIPR36250", false)]
    [InlineData("AGIPR3625", false)]
    [InlineData("AGIPR3625FX", false)]
    [InlineData("AGI-R3625F", false)]
    [InlineData("", false)]
    public void Pan_must_match_the_indian_pan_format_case_insensitively(string value, bool valid)
        => Assert.Equal(valid, SensitiveValidator.Validate(new PisSensitiveIdentifiersInput { PanNumber = value }).IsValid);

    [Theory]
    [InlineData("agipr3625f", "AGIPR3625F")]
    [InlineData("  AgiPR3625f ", "AGIPR3625F")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Pan_is_normalised_to_trimmed_upper_case(string? input, string? expected)
        => Assert.Equal(expected, PisNormalizer.Pan(input));

    [Theory]
    [InlineData("123546789532", true)]
    [InlineData("000000000000", true)]   // leading zeros are kept because it is text
    [InlineData("12354678953", false)]   // 11
    [InlineData("1235467895321", false)] // 13
    [InlineData("12354678953A", false)]
    [InlineData("1235 4678 9532", false)]
    [InlineData("", false)]
    public void Aadhaar_must_be_exactly_twelve_digits(string value, bool valid)
        => Assert.Equal(valid, SensitiveValidator.Validate(new PisSensitiveIdentifiersInput { AadhaarNumber = value }).IsValid);

    [Theory]
    [InlineData("321654987321", true)]
    [InlineData("32165498732", false)]
    [InlineData("32165498732X", false)]
    [InlineData("", false)]
    public void Pran_follows_the_provisional_twelve_digit_format(string value, bool valid)
        => Assert.Equal(valid, SensitiveValidator.Validate(new PisSensitiveIdentifiersInput { PranNumber = value }).IsValid);

    [Theory]
    [InlineData("30927989329", true)]               // 11 digits (workbook sample)
    [InlineData("123456789", true)]                 // 9
    [InlineData("123456789012345678", true)]        // 18
    [InlineData("12345678", false)]                 // 8
    [InlineData("1234567890123456789", false)]      // 19
    [InlineData("3092-7989-329", false)]
    [InlineData("3092798932A", false)]
    [InlineData("", false)]
    public void Salary_account_number_is_digits_only_with_a_variable_length(string value, bool valid)
        => Assert.Equal(valid, SensitiveValidator.Validate(new PisSensitiveIdentifiersInput { SalaryAccountNumber = value }).IsValid);

    [Fact]
    public void All_sensitive_identifiers_are_optional()
        => Assert.True(SensitiveValidator.Validate(new PisSensitiveIdentifiersInput()).IsValid);

    [Fact]
    public void Validation_messages_never_echo_the_sensitive_values()
    {
        PisSensitiveIdentifiersInput input = new()
        {
            AadhaarNumber = "SECRET-AADHAAR-1", PanNumber = "SECRET-PAN-2", PranNumber = "SECRET-PRAN-3", SalaryAccountNumber = "SECRET-ACCT-4"
        };

        ValidationResult result = SensitiveValidator.Validate(input);

        Assert.Equal(4, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.DoesNotContain("SECRET", e.ErrorMessage));
    }

    // ── Height and weight ──

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(176, true)]
    [InlineData(255, true)]
    public void Height_is_a_positive_whole_number_of_centimetres(int value, bool valid)
    {
        UserPis p = Valid(); p.HeightCm = (byte)value;
        Assert.Equal(valid, Validator.Validate(p).IsValid);
    }

    [Fact]
    public void Height_cannot_hold_fractions_because_the_property_is_an_integer_type()
    {
        Type? underlying = Nullable.GetUnderlyingType(typeof(UserPis).GetProperty(nameof(UserPis.HeightCm))!.PropertyType);
        Assert.Equal(typeof(byte), underlying);
    }

    [Theory]
    [InlineData("81.96", true)]
    [InlineData("100.96", true)]
    [InlineData("70", true)]
    [InlineData("0.01", true)]
    [InlineData("81.960", true)]     // trailing zero is not a third decimal place
    [InlineData("999.99", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("81.961", false)]    // three decimals
    [InlineData("1000", false)]      // beyond DECIMAL(5,2)
    [InlineData("1000.5", false)]
    public void Weight_is_positive_with_at_most_two_decimals_within_decimal_5_2(string value, bool valid)
    {
        UserPis p = Valid(); p.WeightKg = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(valid, Validator.Validate(p).IsValid);
    }

    // ── Enums ──

    [Fact]
    public void Defined_enum_values_are_accepted_and_undefined_ones_rejected()
    {
        foreach (PersonnelCategory c in Enum.GetValues<PersonnelCategory>())
        {
            UserPis p = Valid(); p.Category = c;
            Assert.True(Validator.Validate(p).IsValid);
        }
        foreach (BloodGroupType b in Enum.GetValues<BloodGroupType>())
        {
            UserPis p = Valid(); p.BloodGroup = b;
            Assert.True(Validator.Validate(p).IsValid);
        }
        foreach (MobileAppActivationStatus s in Enum.GetValues<MobileAppActivationStatus>())
        {
            UserPis p = Valid(); p.MobileAppActivationStatus = s;
            Assert.True(Validator.Validate(p).IsValid);
        }

        UserPis badCategory = Valid(); badCategory.Category = (PersonnelCategory)0;
        UserPis badCategoryHigh = Valid(); badCategoryHigh.Category = (PersonnelCategory)5; // EWS is not an approved category
        UserPis badBlood = Valid(); badBlood.BloodGroup = (BloodGroupType)9;
        UserPis badStatus = Valid(); badStatus.MobileAppActivationStatus = (MobileAppActivationStatus)3;
        Assert.True(HasErrorOn(Validator.Validate(badCategory), nameof(UserPis.Category)));
        Assert.True(HasErrorOn(Validator.Validate(badCategoryHigh), nameof(UserPis.Category)));
        Assert.True(HasErrorOn(Validator.Validate(badBlood), nameof(UserPis.BloodGroup)));
        Assert.True(HasErrorOn(Validator.Validate(badStatus), nameof(UserPis.MobileAppActivationStatus)));
    }

    [Fact]
    public void New_record_defaults_to_pending_and_nullable_enums_may_be_absent()
    {
        UserPis p = Valid();
        Assert.Equal(MobileAppActivationStatus.Pending, p.MobileAppActivationStatus);
        Assert.Null(p.Category);
        Assert.Null(p.BloodGroup);
        Assert.True(Validator.Validate(p).IsValid);
    }

    // ── Dates ──

    [Fact]
    public void Date_relationships_are_checked_only_when_both_dates_are_present()
    {
        UserPis ok = Valid();
        ok.DateOfBirth = new DateOnly(1992, 2, 4);
        ok.DateOfEnrollmentInGovtService = new DateOnly(2012, 3, 4);
        ok.DateOfSuperannuation = new DateOnly(2052, 2, 29);
        ok.DateOfInductionInNsg = new DateOnly(2024, 2, 4);
        ok.DateOfRepatriation = new DateOnly(2031, 1, 31);
        Assert.True(Validator.Validate(ok).IsValid);

        UserPis partial = Valid(); partial.DateOfEnrollmentInGovtService = new DateOnly(2012, 3, 4); // no birth date
        Assert.True(Validator.Validate(partial).IsValid);

        UserPis enrolledBeforeBirth = Valid(); enrolledBeforeBirth.DateOfBirth = new DateOnly(1992, 2, 4); enrolledBeforeBirth.DateOfEnrollmentInGovtService = new DateOnly(1990, 1, 1);
        UserPis retiredBeforeBirth = Valid(); retiredBeforeBirth.DateOfBirth = new DateOnly(1992, 2, 4); retiredBeforeBirth.DateOfSuperannuation = new DateOnly(1992, 2, 4);
        UserPis repatriatedBeforeInduction = Valid(); repatriatedBeforeInduction.DateOfInductionInNsg = new DateOnly(2024, 2, 4); repatriatedBeforeInduction.DateOfRepatriation = new DateOnly(2023, 1, 1);
        UserPis bornTomorrow = Valid(); bornTomorrow.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);

        Assert.True(HasErrorOn(Validator.Validate(enrolledBeforeBirth), nameof(UserPis.DateOfEnrollmentInGovtService)));
        Assert.True(HasErrorOn(Validator.Validate(retiredBeforeBirth), nameof(UserPis.DateOfSuperannuation)));
        Assert.True(HasErrorOn(Validator.Validate(repatriatedBeforeInduction), nameof(UserPis.DateOfRepatriation)));
        Assert.True(HasErrorOn(Validator.Validate(bornTomorrow), nameof(UserPis.DateOfBirth)));
    }

    [Fact]
    public void Side_stepping_date_and_unit_are_independent_and_optional()
    {
        UserPis dateOnly = Valid(); dateOnly.SideSteppedOn = new DateOnly(2025, 1, 10);
        UserPis unitOnly = Valid(); unitOnly.SideSteppedFromNsgUnitId = 3;
        Assert.True(Validator.Validate(dateOnly).IsValid);
        Assert.True(Validator.Validate(unitOnly).IsValid);
    }

    // ── Other columns ──

    [Theory]
    [InlineData("799144", true)]
    [InlineData("099144", false)]
    [InlineData("79914", false)]
    [InlineData("7991445", false)]
    [InlineData("79914A", false)]
    public void Pin_code_is_six_digits_not_starting_with_zero(string value, bool valid)
    {
        UserPis p = Valid(); p.PermanentPinCode = value;
        Assert.Equal(valid, Validator.Validate(p).IsValid);
    }

    [Fact]
    public void Quantities_cannot_be_negative_and_text_columns_respect_their_lengths()
    {
        UserPis p = Valid();
        p.NumberOfChildren = -1; p.EarnedLeaveDue = -0.5m; p.HalfPayLeaveDue = 100000m;
        p.FullName = new string('x', 201); p.PermanentDistrict = new string('x', 101); p.PaternityLeaveAvailed = new string('x', 101);

        ValidationResult result = Validator.Validate(p);

        foreach (string property in new[] { nameof(UserPis.NumberOfChildren), nameof(UserPis.EarnedLeaveDue), nameof(UserPis.HalfPayLeaveDue),
                     nameof(UserPis.FullName), nameof(UserPis.PermanentDistrict), nameof(UserPis.PaternityLeaveAvailed) })
            Assert.True(HasErrorOn(result, property), property);
    }
}
