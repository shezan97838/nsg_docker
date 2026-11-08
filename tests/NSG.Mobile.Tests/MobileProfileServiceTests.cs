using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Profile.DTOs;
using NSG.Mobile.Application.Profile.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>GET /api/v1/profile logic over in-memory fakes: identity from the token only, NSG-number link, full DTO mapping.</summary>
public class MobileProfileServiceTests
{
    private const string Cipher = "CIPHER-SECRET-VALUE";
    private const string Aadhaar = "123546789532", Pan = "AGIPR3625F", Pran = "321654987321", Account = "30927989329";

    // Real AES with a throw-away in-memory key, so the tests exercise the real decryption path.
    private static readonly AesEncryptionService Aes = new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private sealed class CapturingLogger : ILogger<MobileProfileService>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + exception);
    }

    private static UserPis Pis(long id, string nsg, string name, string mobile) => new()
    {
        Id = id,
        NsgNumber = nsg,
        ServiceNumber = "SVC-" + nsg,
        FullName = name,
        MobileNumber = mobile,
        DateOfBirth = new DateOnly(1990, 5, 15),
        DateOfInductionInNsg = new DateOnly(2010, 1, 1),
        DateOfSuperannuation = new DateOnly(2050, 5, 15),
        DateOfEnrollmentInGovtService = new DateOnly(2008, 6, 20),
        DateOfRepatriation = new DateOnly(2015, 3, 10),
        SideSteppedDetails = "01.07.2012 FROM Unit Six",
        DateOfAme = new DateOnly(2022, 4, 15),
        DateOfImmovablePropertyReturn = new DateOnly(2023, 1, 31),
        EarnedLeaveDue = "12.5",
        HalfPayLeaveDue = "4.0",
        PaternityLeaveAvailed = "0",
        MaternityLeaveAvailed = null,
        ChildCareLeaveAvailed = "2 TIMES",
        NsgRank = "Inspector",
        ParentDepartmentRank = "Havildar",
        NsgUnit = "Unit Four",
        Education = "BA",
        Religion = "HINDU",
        FatherName = "Ram Saran",
        MotherName = "Savitri Devi",
        SpouseName = "Laxmi",
        NextOfKinMobileNumber = "9876543210",
        NumberOfChildren = "2",
        Gender = "Female",
        Category = "GEN",
        BloodGroup = "O+",
        HeightCm = "172",
        WeightKg = "70.5",
        PermanentVillage = "Rampur",
        PermanentPostOffice = "Rampur PO",
        PermanentPoliceStation = "Rampur PS",
        PermanentDistrict = "Muzaffarnagar",
        PermanentState = "Uttar Pradesh",
        PermanentPinCode = "251001",
        AadhaarNumberEncrypted = Aes.Encrypt(Aadhaar),
        PanNumberEncrypted = Aes.Encrypt(Pan),
        PranNumberEncrypted = Aes.Encrypt(Pran),
        SalaryAccountNumberEncrypted = Aes.Encrypt(Account)
    };
    private static (MobileProfileService Service, FakeCurrentUser User, FakeProfileRepository Profiles, FakePisRepository PisRepo) Build()
    {
        FakeProfileRepository profiles = new(new FakeGrantRepository(new TestClock()));
        FakePisRepository pis = new();
        FakeCurrentUser user = new();
        return (new MobileProfileService(user, profiles, pis, Aes, NullLogger<MobileProfileService>.Instance), user, profiles, pis);
    }

    [Fact]
    public async Task Returns_the_pis_details_found_through_the_nsg_number_not_the_profile_id()
    {
        var (service, user, profiles, pis) = Build();
        // Profile id 1 deliberately equals the *other* person's PIS id.
        pis.Rows.Add(Pis(1, "NSG-OTHER", "Someone Else", "9000000001"));
        pis.Rows.Add(Pis(2, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 1, NsgNumber = "NSG-MINE", MobileNumber = "9000000002" });
        user.ProfileId = 1;

        Result<MobileProfileResponseDto> r = await service.GetMyProfileAsync();

        Assert.True(r.IsSuccess);
        Assert.Equal("NSG-MINE", r.Data!.NsgNumber);
        Assert.Equal("My Name", r.Data.FullName);
        Assert.Equal("Inspector", r.Data.NsgRank);
        Assert.Equal("Unit Four", r.Data.NsgUnit);
        Assert.Equal(new DateOnly(1990, 5, 15), r.Data.DateOfBirth);
        Assert.Equal("9000000002", r.Data.MobileNumber);
    }

    [Fact]
    public async Task All_lookup_ids_and_names_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal("Inspector", dto.NsgRank);
        Assert.Equal("Havildar", dto.ParentDepartmentRank);
        Assert.Equal("Unit Four", dto.NsgUnit);
        Assert.Equal("01.07.2012 FROM Unit Six", dto.SideSteppedDetails);   // one free-text value, never split
        Assert.Equal("BA", dto.Education);
        Assert.Equal("HINDU", dto.Religion);
        Assert.Equal("Uttar Pradesh", dto.PermanentState);
    }

    [Fact]
    public async Task All_personal_and_family_fields_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal("Female", dto.Gender);
        Assert.Equal("GEN", dto.Category);
        Assert.Equal("Ram Saran", dto.FatherName); Assert.Equal("Savitri Devi", dto.MotherName);
        Assert.Equal("Laxmi", dto.SpouseName); Assert.Equal("9876543210", dto.NextOfKinMobileNumber);
        Assert.Equal("2", dto.NumberOfChildren);
        Assert.Equal("O+", dto.BloodGroup);
        Assert.Equal("172", dto.HeightCm); Assert.Equal("70.5", dto.WeightKg);
    }

    [Fact]
    public async Task Text_columns_are_returned_exactly_as_stored_with_no_numeric_conversion()
    {
        var (service, user, profiles, pis) = Build();
        UserPis row = Pis(1, "NSG-MINE", "My Name", "9000000002");
        row.HeightCm = "5'10\""; row.WeightKg = "81.50"; row.NumberOfChildren = "02"; row.EarnedLeaveDue = "220 DAYS";
        row.HalfPayLeaveDue = "00360"; row.Gender = "M"; row.Category = "General"; row.BloodGroup = "O POSITIVE";
        row.PermanentPinCode = "0123456"; row.NextOfKinMobileNumber = "+91 98765 43210";
        pis.Rows.Add(row);
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal("5'10\"", dto.HeightCm); Assert.Equal("81.50", dto.WeightKg); Assert.Equal("02", dto.NumberOfChildren);
        Assert.Equal("220 DAYS", dto.EarnedLeaveDue); Assert.Equal("00360", dto.HalfPayLeaveDue);
        Assert.Equal("M", dto.Gender); Assert.Equal("General", dto.Category); Assert.Equal("O POSITIVE", dto.BloodGroup);
        Assert.Equal("0123456", dto.PermanentPinCode); Assert.Equal("+91 98765 43210", dto.NextOfKinMobileNumber);
    }

    [Fact]
    public void The_profile_response_has_no_lookup_ids_or_legacy_side_stepped_parts()
    {
        string[] names = typeof(MobileProfileResponseDto).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.EndsWith("Id", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.EndsWith("Name", StringComparison.Ordinal) && n != "FullName" && n != "FatherName" && n != "MotherName" && n != "SpouseName");
        Assert.DoesNotContain("SideSteppedOn", names);
        Assert.Contains("SideSteppedDetails", names);
        Assert.All(typeof(MobileProfileResponseDto).GetProperties().Where(p => p.Name is "Gender" or "Category" or "BloodGroup" or "HeightCm" or "WeightKg"
            or "NumberOfChildren" or "EarnedLeaveDue" or "HalfPayLeaveDue"), p => Assert.Equal(typeof(string), p.PropertyType));
    }

    [Fact]
    public async Task All_date_fields_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal(new DateOnly(1990, 5, 15), dto.DateOfBirth);
        Assert.Equal(new DateOnly(2010, 1, 1), dto.DateOfInductionInNsg);
        Assert.Equal(new DateOnly(2050, 5, 15), dto.DateOfSuperannuation);
        Assert.Equal(new DateOnly(2008, 6, 20), dto.DateOfEnrollmentInGovtService);
        Assert.Equal(new DateOnly(2015, 3, 10), dto.DateOfRepatriation);
        Assert.Equal("01.07.2012 FROM Unit Six", dto.SideSteppedDetails);
        Assert.Equal(new DateOnly(2022, 4, 15), dto.DateOfAme);
        Assert.Equal(new DateOnly(2023, 1, 31), dto.DateOfImmovablePropertyReturn);
    }

    [Fact]
    public async Task All_address_fields_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal("Rampur", dto.PermanentVillage); Assert.Equal("Rampur PO", dto.PermanentPostOffice);
        Assert.Equal("Rampur PS", dto.PermanentPoliceStation); Assert.Equal("Muzaffarnagar", dto.PermanentDistrict);
        Assert.Equal("251001", dto.PermanentPinCode);
    }

    [Fact]
    public async Task All_leave_summary_fields_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal("12.5", dto.EarnedLeaveDue); Assert.Equal("4.0", dto.HalfPayLeaveDue);
        Assert.Equal("0", dto.PaternityLeaveAvailed);
        Assert.Null(dto.MaternityLeaveAvailed);
        Assert.Equal("2 TIMES", dto.ChildCareLeaveAvailed);
    }

    [Fact]
    public async Task Null_optional_lookups_do_not_cause_failure()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(new UserPis
        {
            Id = 1, NsgNumber = "NSG-BARE", ServiceNumber = "SVC-1",
            AadhaarNumberEncrypted = null, PanNumberEncrypted = null,
            PranNumberEncrypted = null, SalaryAccountNumberEncrypted = null
        });
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-BARE" });
        user.ProfileId = 9;

        Result<MobileProfileResponseDto> r = await service.GetMyProfileAsync();

        Assert.True(r.IsSuccess);
        MobileProfileResponseDto dto = r.Data!;
        Assert.Null(dto.NsgRank);
        Assert.Null(dto.ParentDepartmentRank);
        Assert.Null(dto.NsgUnit);
        
        Assert.Null(dto.Education);
        Assert.Null(dto.Religion);
        Assert.Null(dto.PermanentState);
        Assert.Null(dto.DateOfBirth); Assert.Null(dto.Category);
        Assert.Null(dto.BloodGroup); Assert.Null(dto.HeightCm); Assert.Null(dto.WeightKg);
        Assert.Null(dto.Gender);   // records that predate the field
    }

    [Fact]
    public async Task The_response_returns_decrypted_identifiers_but_never_ciphertext_hashes_or_admin_fields()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE", MpinHash = "MPIN-HASH-VALUE", PasswordHash = "PW-HASH-VALUE" });
        user.ProfileId = 9;
        UserPis stored = pis.Rows.Single();
        string[] cipherBefore = { stored.AadhaarNumberEncrypted!, stored.PanNumberEncrypted!, stored.PranNumberEncrypted!, stored.SalaryAccountNumberEncrypted! };

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;
        string json = JsonSerializer.Serialize(dto);

        Assert.Equal(Aadhaar, dto.AadhaarNumber);
        Assert.Equal(Pan, dto.PanNumber);
        Assert.Equal(Pran, dto.PranNumber);
        Assert.Equal(Account, dto.SalaryAccountNumber);
        Assert.Equal("Female", dto.Gender);
        foreach (string cipher in cipherBefore.Append(Cipher))
            Assert.DoesNotContain(cipher, json);
        Assert.DoesNotContain("HASH-VALUE", json);
        // Only the four identifiers may match; hashes, ciphertext, credentials and admin-only fields stay out.
        foreach (PropertyInfo p in typeof(MobileProfileResponseDto).GetProperties())
            Assert.DoesNotMatch("(?i)hash|mpin|password|encrypted|isactive|mobilestatus|createdat|updatedat|createdby|updatedby", p.Name);
        Assert.Equal(new[] { "AadhaarNumber", "PanNumber", "PranNumber", "SalaryAccountNumber" },
            typeof(MobileProfileResponseDto).GetProperties().Select(p => p.Name).Where(n => Regex.IsMatch(n, "(?i)aadhaar|pan|pran|salary")).OrderBy(n => n));
        // Decryption is read-only: the stored ciphertext is exactly what it was.
        Assert.Equal(cipherBefore, new[] { stored.AadhaarNumberEncrypted!, stored.PanNumberEncrypted!, stored.PranNumberEncrypted!, stored.SalaryAccountNumberEncrypted! });
    }

    [Fact]
    public async Task Missing_identifiers_are_null_and_the_rest_of_the_profile_is_still_returned()
    {
        var (service, user, profiles, pis) = Build();
        UserPis row = Pis(1, "NSG-MINE", "My Name", "9000000002");
        row.AadhaarNumberEncrypted = null; row.PanNumberEncrypted = null;   // PRAN and account stay set
        pis.Rows.Add(row);
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Null(dto.AadhaarNumber); Assert.Null(dto.PanNumber);
        Assert.Equal(Pran, dto.PranNumber); Assert.Equal(Account, dto.SalaryAccountNumber);
        Assert.Equal("My Name", dto.FullName);
    }

    [Theory]
    [InlineData("CIPHER-SECRET-VALUE")]                       // not base64
    [InlineData("QUJD")]                                      // valid base64 but shorter than the IV
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // right shape, wrong key / bad padding
    public async Task An_unreadable_stored_value_gives_null_never_ciphertext_and_logs_no_values(string unreadable)
    {
        FakeProfileRepository profiles = new(new FakeGrantRepository(new TestClock()));
        FakePisRepository pis = new();
        FakeCurrentUser user = new();
        CapturingLogger logger = new();
        MobileProfileService service = new(user, profiles, pis, Aes, logger);
        UserPis row = Pis(1, "NSG-MINE", "My Name", "9000000002");
        row.AadhaarNumberEncrypted = unreadable;    // only this one is broken
        pis.Rows.Add(row);
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        Result<MobileProfileResponseDto> r = await service.GetMyProfileAsync();

        Assert.True(r.IsSuccess);                                   // does not fail the request
        Assert.Null(r.Data!.AadhaarNumber);                         // no ciphertext fallback
        Assert.Equal(Pan, r.Data.PanNumber);                        // other identifiers unaffected
        Assert.Equal("My Name", r.Data.FullName);
        Assert.DoesNotContain(unreadable, JsonSerializer.Serialize(r.Data));
        Assert.Equal(unreadable, row.AadhaarNumberEncrypted);       // stored value untouched
        string logged = string.Join("|", logger.Messages);
        Assert.Contains("Aadhaar", logged);                         // the failure is logged by field name only
        Assert.DoesNotContain(unreadable, logged);
        foreach (string secret in new[] { Aadhaar, Pan, Pran, Account })
            Assert.DoesNotContain(secret, logged);
    }

    [Fact]
    public async Task A_value_encrypted_with_a_different_key_is_not_revealed()
    {
        AesEncryptionService otherKey = new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        var (service, user, profiles, pis) = Build();
        UserPis row = Pis(1, "NSG-MINE", "My Name", "9000000002");
        row.SalaryAccountNumberEncrypted = otherKey.Encrypt(Account);
        pis.Rows.Add(row);
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        // AES-CBC has no integrity tag, so a wrong key either fails padding (null) or yields garbage; it must never yield the real value.
        Assert.NotEqual(Account, dto.SalaryAccountNumber);
        Assert.NotEqual(row.SalaryAccountNumberEncrypted, dto.SalaryAccountNumber);
    }

    [Fact]
    public async Task Without_a_mobile_profile_claim_the_caller_is_unauthorized_and_nothing_is_looked_up()
    {
        var (service, user, _, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        user.ProfileId = null;      // staff token or no token

        Result<MobileProfileResponseDto> r = await service.GetMyProfileAsync();

        Assert.Equal(ApiErrorCodes.Unauthorized, r.ErrorCode);
        Assert.Equal(0, pis.NsgLookups);
    }

    [Fact]
    public async Task A_missing_or_inactive_profile_is_unauthorized()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        user.ProfileId = 77;
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetMyProfileAsync()).ErrorCode);

        profiles.Profiles.Add(new MobileUserProfile { Id = 77, NsgNumber = "NSG-MINE", IsActive = false });
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetMyProfileAsync()).ErrorCode);
    }

    [Fact]
    public async Task A_missing_pis_record_is_not_found_with_a_message()
    {
        var (service, user, profiles, _) = Build();
        profiles.Profiles.Add(new MobileUserProfile { Id = 5, NsgNumber = "NSG-GONE" });
        user.ProfileId = 5;

        Result<MobileProfileResponseDto> r = await service.GetMyProfileAsync();

        Assert.Equal(ApiErrorCodes.NotFound, r.ErrorCode);
        Assert.Equal(MobileProfileService.PisRecordNotFoundMessage, r.ErrorMessage);
    }

    [Fact]
    public async Task Two_users_each_get_only_their_own_record()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-A", "Alpha", "9000000001"));
        pis.Rows.Add(Pis(2, "NSG-B", "Bravo", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 10, NsgNumber = "NSG-A" });
        profiles.Profiles.Add(new MobileUserProfile { Id = 20, NsgNumber = "NSG-B" });

        user.ProfileId = 10;
        Assert.Equal("Alpha", (await service.GetMyProfileAsync()).Data!.FullName);
        user.ProfileId = 20;
        Assert.Equal("Bravo", (await service.GetMyProfileAsync()).Data!.FullName);
    }

    [Fact]
    public async Task ProfileController_returns_success_envelope_with_message_and_data()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 1, NsgNumber = "NSG-MINE", MobileNumber = "9000000002" });
        user.ProfileId = 1;

        ProfileController controller = new(service);
        IActionResult actionResult = await controller.GetMyProfile(default);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(actionResult);
        ApiResponse<MobileProfileResponseDto> envelope = Assert.IsType<ApiResponse<MobileProfileResponseDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal("Personnel details fetched successfully", envelope.Message);
        Assert.NotNull(envelope.Data);
        Assert.Equal("NSG-MINE", envelope.Data!.NsgNumber);
        Assert.Equal("My Name", envelope.Data.FullName);
    }

    [Fact]
    public async Task ProfileController_returns_problem_details_when_profile_not_found()
    {
        var (service, user, profiles, _) = Build();
        profiles.Profiles.Add(new MobileUserProfile { Id = 5, NsgNumber = "NSG-GONE" });
        user.ProfileId = 5;

        ProfileController controller = new(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        IActionResult actionResult = await controller.GetMyProfile(default);

        ObjectResult objectResult = Assert.IsAssignableFrom<ObjectResult>(actionResult);
        Assert.Equal(404, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }
}
