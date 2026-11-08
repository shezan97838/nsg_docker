using System.Text.Json;
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

    private static readonly UpdateUserPisRequestValidator RequestValidator = new();

    // The individual's mobile number is mandatory (users_pis.mobile_number is NOT NULL).
    private static UserPis Valid() => new() { NsgNumber = "123456", ServiceNumber = "123456789", MobileNumber = "9876543210" };

    private static UpdateUserPisRequestDto Dto() => new() { ServiceNumber = "123456789", MobileNumber = "9876543210", IsActive = true };

    private static bool HasErrorOn(ValidationResult result, string property) => result.Errors.Any(e => e.PropertyName == property);

    // ── Identifiers ──

    [Fact]
    public void Minimal_record_with_only_the_nsg_number_and_mobile_number_is_valid()
    {
        UserPis p = new() { NsgNumber = "123456", MobileNumber = "9876543210" };   // service number is optional
        Assert.True(Validator.Validate(p).IsValid);
    }

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
    [InlineData("5876543210", false)]   // the API only accepts numbers that can register in the mobile app (start 6-9)
    [InlineData("987654321", false)]    // nine digits
    [InlineData("98765432101", false)]  // eleven digits
    [InlineData("+919876543210", false)] // no country code
    [InlineData("98765 43210", false)]
    [InlineData("98765abcde", false)]
    [InlineData("9876543210\n", false)]
    [InlineData("", false)]
    public void The_individuals_mobile_number_must_be_ten_digits(string value, bool valid)
    {
        UserPis own = Valid(); own.MobileNumber = value;
        Assert.Equal(valid, !HasErrorOn(Validator.Validate(own), nameof(UserPis.MobileNumber)));
    }

    [Theory]
    [InlineData("9876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("0120-2345678")]
    public void Next_of_kin_mobile_is_free_text_up_to_15_characters(string value)
    {
        UserPis row = Valid(); row.NextOfKinMobileNumber = value;
        UpdateUserPisRequestDto request = Dto(); request.NextOfKinMobileNumber = value;
        Assert.True(Validator.Validate(row).IsValid);
        Assert.True(RequestValidator.Validate(request).IsValid);

        row.NextOfKinMobileNumber = new string('9', 16); request.NextOfKinMobileNumber = new string('9', 16);
        Assert.True(HasErrorOn(Validator.Validate(row), nameof(UserPis.NextOfKinMobileNumber)));
        Assert.Contains(RequestValidator.Validate(request).Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.NextOfKinMobileNumber));
    }
    [Fact]
    public void The_individuals_mobile_number_is_mandatory_but_next_of_kin_is_optional()
    {
        UserPis noNextOfKin = Valid(); noNextOfKin.NextOfKinMobileNumber = null;
        UserPis noMobile = Valid(); noMobile.MobileNumber = string.Empty;
        Assert.True(Validator.Validate(noNextOfKin).IsValid);
        Assert.True(HasErrorOn(Validator.Validate(noMobile), nameof(UserPis.MobileNumber)));

        // Create requires it; update keeps the stored number when it is omitted.
        CreateUserPisRequestDto create = new() { NsgNumber = "N-1", ServiceNumber = "S-1", Gender = "Male" };
        Assert.Contains(new CreateUserPisRequestValidator().Validate(create).Errors, e => e.PropertyName == nameof(CreateUserPisRequestDto.MobileNumber));
        UpdateUserPisRequestDto update = Dto(); update.MobileNumber = null;
        Assert.True(RequestValidator.Validate(update).IsValid);
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

    // ── Height and weight (the request still takes numbers; the row stores text) ──

    [Theory]
    [InlineData("172", true)]
    [InlineData("5'10\"", true)]
    [InlineData("0", true)]          // nothing is parsed or range-checked: the value is text
    [InlineData("-3", true)]
    [InlineData("81.50", true)]
    [InlineData("1000.5 KG", true)]
    public void Height_and_weight_are_stored_as_sent_without_numeric_validation(string value, bool valid)
    {
        UpdateUserPisRequestDto request = Dto(); request.HeightCm = value; request.WeightKg = value;
        Assert.Equal(valid, RequestValidator.Validate(request).IsValid);
    }

    [Fact]
    public void Height_and_weight_only_have_a_length_limit_of_50()
    {
        UpdateUserPisRequestDto request = Dto(); request.HeightCm = new string('9', 51); request.WeightKg = new string('9', 51);

        ValidationResult result = RequestValidator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.HeightCm));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.WeightKg));
    }

    // ── Text model shape ──

    [Fact]
    public void Exactly_the_seven_date_columns_are_dates_and_every_other_workbook_column_is_text()
    {
        string[] dates =
        {
            nameof(UserPis.DateOfInductionInNsg), nameof(UserPis.DateOfRepatriation), nameof(UserPis.DateOfBirth),
            nameof(UserPis.DateOfEnrollmentInGovtService), nameof(UserPis.DateOfSuperannuation),
            nameof(UserPis.DateOfImmovablePropertyReturn), nameof(UserPis.DateOfAme)
        };
        string[] text =
        {
            nameof(UserPis.NsgNumber), nameof(UserPis.ServiceNumber), nameof(UserPis.NsgRank), nameof(UserPis.ParentDepartmentRank),
            nameof(UserPis.FullName), nameof(UserPis.NsgUnit), nameof(UserPis.Education),
            nameof(UserPis.SideSteppedDetails), nameof(UserPis.Gender), nameof(UserPis.Category), nameof(UserPis.Religion),
            nameof(UserPis.BloodGroup), nameof(UserPis.HeightCm), nameof(UserPis.WeightKg), nameof(UserPis.MobileNumber),
            nameof(UserPis.EarnedLeaveDue), nameof(UserPis.HalfPayLeaveDue), nameof(UserPis.PaternityLeaveAvailed),
            nameof(UserPis.MaternityLeaveAvailed), nameof(UserPis.ChildCareLeaveAvailed), nameof(UserPis.FatherName), nameof(UserPis.MotherName),
            nameof(UserPis.SpouseName), nameof(UserPis.NextOfKinMobileNumber), nameof(UserPis.NumberOfChildren), nameof(UserPis.PermanentVillage),
            nameof(UserPis.PermanentPostOffice), nameof(UserPis.PermanentPoliceStation), nameof(UserPis.PermanentDistrict),
            nameof(UserPis.PermanentState), nameof(UserPis.PermanentPinCode), nameof(UserPis.AadhaarNumberEncrypted),
            nameof(UserPis.PanNumberEncrypted), nameof(UserPis.PranNumberEncrypted), nameof(UserPis.SalaryAccountNumberEncrypted)
        };

        foreach (string name in dates)
            Assert.Equal(typeof(DateOnly), Nullable.GetUnderlyingType(typeof(UserPis).GetProperty(name)!.PropertyType));
        foreach (string name in text)
            Assert.Equal(typeof(string), typeof(UserPis).GetProperty(name)!.PropertyType);
    }

    [Fact]
    public void The_entity_has_no_lookup_ids_navigations_or_hash_columns()
    {
        string[] names = typeof(UserPis).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.EndsWith("Id", StringComparison.Ordinal) && n != nameof(UserPis.Id));
        Assert.DoesNotContain(names, n => n.EndsWith("Hash", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(UserPis).GetProperties(), p => p.PropertyType.Namespace == "NSG.Mobile.Domain.Entities");
        Assert.DoesNotContain("SideSteppedOn", names);
    }

    [Theory]
    [InlineData("Male")]
    [InlineData("M")]
    [InlineData("transgender woman")]
    [InlineData("1")]
    public void Gender_category_and_blood_group_accept_any_text_and_store_it_as_sent(string text)
    {
        UserPis row = new();
        UpdateUserPisRequestDto request = Dto(); request.Gender = "  " + text + " "; request.Category = text; request.BloodGroup = text;

        Assert.True(RequestValidator.Validate(request).IsValid);
        request.ApplyTo(row);

        Assert.Equal(text, row.Gender);       // trimmed, never re-spelled or mapped to a list
        Assert.Equal(text, row.Category);
        Assert.Equal(text, row.BloodGroup);
    }

    [Fact]
    public void Gender_is_required_on_create_but_any_text_will_do_and_an_omitted_gender_keeps_the_stored_one_on_update()
    {
        CreateUserPisRequestDto create = new() { NsgNumber = "N-1", ServiceNumber = "S-1", MobileNumber = "9876543210" };
        Assert.Contains(new CreateUserPisRequestValidator().Validate(create).Errors, e => e.PropertyName == nameof(CreateUserPisRequestDto.Gender));
        create.Gender = "whatever the workbook says";
        Assert.True(new CreateUserPisRequestValidator().Validate(create).IsValid);

        UserPis row = new() { Gender = "Female" };
        Dto().ApplyTo(row);   // gender omitted
        Assert.Equal("Female", row.Gender);
    }

    [Fact]
    public void The_free_text_columns_only_have_their_column_length_limits()
    {
        UpdateUserPisRequestDto request = Dto(); request.Gender = new string('x', 31); request.Category = new string('x', 31); request.BloodGroup = new string('x', 21);

        ValidationResult result = RequestValidator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.Gender));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.Category));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.BloodGroup));
    }

    [Fact]
    public void The_activation_status_is_checked_on_the_entity()
    {
        UserPis badStatus = Valid(); badStatus.MobileAppActivationStatus = (MobileAppActivationStatus)3;
        Assert.True(HasErrorOn(Validator.Validate(badStatus), nameof(UserPis.MobileAppActivationStatus)));
        foreach (MobileAppActivationStatus s in Enum.GetValues<MobileAppActivationStatus>())
        {
            UserPis p = Valid(); p.MobileAppActivationStatus = s;
            Assert.True(Validator.Validate(p).IsValid);
        }
    }
    [Fact]
    public void New_record_defaults_to_pending_and_the_text_columns_may_be_absent()
    {
        UserPis p = Valid();
        Assert.Equal(MobileAppActivationStatus.Pending, p.MobileAppActivationStatus);
        Assert.Null(p.Category);
        Assert.Null(p.BloodGroup);
        Assert.Null(p.Gender);
        Assert.True(Validator.Validate(p).IsValid);
    }

    // ── Dates ──

    [Fact]
    public void Dates_are_not_order_checked_so_unusual_workbook_records_are_accepted()
    {
        UserPis unusual = Valid();
        unusual.DateOfBirth = new DateOnly(1990, 5, 1);
        unusual.DateOfSuperannuation = new DateOnly(1941, 2, 28);          // before birth: exists in the source workbook
        unusual.DateOfEnrollmentInGovtService = new DateOnly(1989, 1, 1);  // before birth
        unusual.DateOfInductionInNsg = new DateOnly(2024, 2, 4);
        unusual.DateOfRepatriation = new DateOnly(2023, 1, 1);             // before induction
        Assert.True(Validator.Validate(unusual).IsValid);

        UpdateUserPisRequestDto request = Dto();
        request.DateOfBirth = unusual.DateOfBirth; request.DateOfSuperannuation = unusual.DateOfSuperannuation;
        request.DateOfEnrollmentInGovtService = unusual.DateOfEnrollmentInGovtService;
        request.DateOfInductionInNsg = unusual.DateOfInductionInNsg; request.DateOfRepatriation = unusual.DateOfRepatriation;
        Assert.True(RequestValidator.Validate(request).IsValid);
    }

    [Fact]
    public void Only_a_birth_date_in_the_future_is_rejected()
    {
        UserPis bornTomorrow = Valid(); bornTomorrow.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        UpdateUserPisRequestDto request = Dto(); request.DateOfBirth = bornTomorrow.DateOfBirth;

        Assert.True(HasErrorOn(Validator.Validate(bornTomorrow), nameof(UserPis.DateOfBirth)));
        Assert.Contains(RequestValidator.Validate(request).Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.DateOfBirth));
    }

    // ── Side-stepped details: one free-text value ──
    [Fact]
    public void Side_stepped_details_are_stored_as_one_trimmed_text_value_and_never_parsed()
    {
        UserPis row = new();
        UpdateUserPisRequestDto request = Dto(); request.SideSteppedDetails = " 10.01.2025 FROM 11 SRG (verbal order) ";

        request.ApplyTo(row);
        Assert.Equal("10.01.2025 FROM 11 SRG (verbal order)", row.SideSteppedDetails);

        Dto().ApplyTo(row);
        Assert.Null(row.SideSteppedDetails);   // nothing supplied clears it (PUT is a full replace)

        UpdateUserPisRequestDto tooLong = Dto(); tooLong.SideSteppedDetails = new string('x', 256);
        Assert.Contains(RequestValidator.Validate(tooLong).Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.SideSteppedDetails));
        UserPis longRow = Valid(); longRow.SideSteppedDetails = new string('x', 256);
        Assert.True(HasErrorOn(Validator.Validate(longRow), nameof(UserPis.SideSteppedDetails)));
    }

    [Fact]
    public void Lookup_values_in_a_request_are_stored_as_trimmed_text_and_blank_becomes_null()
    {
        UserPis row = new();
        UpdateUserPisRequestDto request = Dto();
        request.NsgRank = " Inspector "; request.ParentDepartmentRank = "Havildar";
        request.NsgUnit = "13 SRG"; request.Education = "BA"; request.Religion = "HINDU"; request.PermanentState = "Uttar Pradesh";

        request.ApplyTo(row);

        Assert.Equal("Inspector", row.NsgRank); Assert.Equal("Havildar", row.ParentDepartmentRank);
        Assert.Equal("13 SRG", row.NsgUnit);
        Assert.Equal("BA", row.Education); Assert.Equal("HINDU", row.Religion); Assert.Equal("Uttar Pradesh", row.PermanentState);
    }

    [Fact]
    public void Numeric_looking_text_is_stored_exactly_as_sent()
    {
        UserPis row = new();
        UpdateUserPisRequestDto request = Dto();
        request.HeightCm = "172"; request.WeightKg = "81.50"; request.NumberOfChildren = "02"; request.EarnedLeaveDue = "00220"; request.HalfPayLeaveDue = "360.0";

        request.ApplyTo(row);

        Assert.Equal("172", row.HeightCm); Assert.Equal("81.50", row.WeightKg); Assert.Equal("02", row.NumberOfChildren);
        Assert.Equal("00220", row.EarnedLeaveDue); Assert.Equal("360.0", row.HalfPayLeaveDue);   // no leading zero or trailing zero is lost
    }

    [Fact]
    public void A_json_number_is_accepted_for_the_text_columns_and_kept_digit_for_digit()
    {
        JsonSerializerOptions web = new(JsonSerializerDefaults.Web);

        UpdateUserPisRequestDto fromNumbers = JsonSerializer.Deserialize<UpdateUserPisRequestDto>(
            "{\"heightCm\":172,\"weightKg\":81.50,\"numberOfChildren\":2,\"earnedLeaveDue\":220,\"halfPayLeaveDue\":\"360 DAYS\"}", web)!;
        UpdateUserPisRequestDto fromText = JsonSerializer.Deserialize<UpdateUserPisRequestDto>("{\"heightCm\":null}", web)!;

        Assert.Equal("172", fromNumbers.HeightCm);
        Assert.Equal("81.50", fromNumbers.WeightKg);   // the trailing zero survives: nothing goes through a decimal
        Assert.Equal("2", fromNumbers.NumberOfChildren);
        Assert.Equal("220", fromNumbers.EarnedLeaveDue);
        Assert.Equal("360 DAYS", fromNumbers.HalfPayLeaveDue);
        Assert.Null(fromText.HeightCm);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<UpdateUserPisRequestDto>("{\"heightCm\":true}", web));
    }

    [Fact]
    public void The_pis_responses_have_no_lookup_ids_or_legacy_side_stepped_parts()
    {
        foreach (Type type in new[] { typeof(UserPisResponseDto), typeof(UserPisListItemDto), typeof(UserPisWriteDto), typeof(UserPisFilter) })
        {
            string[] names = type.GetProperties().Select(p => p.Name).ToArray();

            Assert.DoesNotContain(names, n => n.EndsWith("Id", StringComparison.Ordinal) && n != "Id" && n != "CreatedBy" && n != "UpdatedBy");
            Assert.DoesNotContain("SideSteppedOn", names);
            Assert.DoesNotContain(names, n => n.StartsWith("SideSteppedFrom", StringComparison.Ordinal));
        }
    }
    [Theory]
    [InlineData("799144", true)]
    [InlineData("099144", true)]        // no format rule: the column is plain text
    [InlineData("79914A", true)]
    [InlineData("7991445678", true)]    // 10 characters
    [InlineData("79914456789", false)]  // 11 characters
    public void Pin_code_is_free_text_up_to_ten_characters(string value, bool valid)
    {
        UpdateUserPisRequestDto request = Dto(); request.PermanentPinCode = value;
        Assert.Equal(valid, RequestValidator.Validate(request).IsValid);
    }
    [Fact]
    public void Row_text_columns_respect_their_lengths_on_the_entity_and_the_request()
    {
        UpdateUserPisRequestDto request = Dto();
        request.NumberOfChildren = new string('9', 51); request.EarnedLeaveDue = new string('9', 101); request.HalfPayLeaveDue = new string('9', 101);
        request.NsgRank = new string('x', 101); request.PermanentState = new string('x', 101);
        ValidationResult requestResult = RequestValidator.Validate(request);
        foreach (string property in new[] { nameof(UpdateUserPisRequestDto.NumberOfChildren), nameof(UpdateUserPisRequestDto.EarnedLeaveDue),
                     nameof(UpdateUserPisRequestDto.HalfPayLeaveDue), nameof(UpdateUserPisRequestDto.NsgRank), nameof(UpdateUserPisRequestDto.PermanentState) })
            Assert.True(requestResult.Errors.Any(e => e.PropertyName == property), property);

        UserPis p = Valid();
        p.FullName = new string('x', 201); p.PermanentDistrict = new string('x', 101); p.PaternityLeaveAvailed = new string('x', 101);
        p.HeightCm = new string('x', 51); p.PermanentPinCode = new string('x', 11); p.Gender = new string('x', 31);
        ValidationResult result = Validator.Validate(p);
        foreach (string property in new[] { nameof(UserPis.FullName), nameof(UserPis.PermanentDistrict), nameof(UserPis.PaternityLeaveAvailed),
                     nameof(UserPis.HeightCm), nameof(UserPis.PermanentPinCode), nameof(UserPis.Gender) })
            Assert.True(HasErrorOn(result, property), property);
    }
}