using System.Reflection;
using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Profile.DTOs;
using NSG.Mobile.Application.Profile.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>GET /api/v1/profile logic over in-memory fakes: identity from the token only, NSG-number link, full DTO mapping.</summary>
public class MobileProfileServiceTests
{
    private const string Cipher = "CIPHER-SECRET-VALUE";

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
        SideSteppedOn = new DateOnly(2012, 7, 1),
        DateOfAme = new DateOnly(2022, 4, 15),
        DateOfImmovablePropertyReturn = new DateOnly(2023, 1, 31),
        EarnedLeaveDue = 12.5m,
        HalfPayLeaveDue = 4.0m,
        PaternityLeaveAvailed = "0",
        MaternityLeaveAvailed = null,
        ChildCareLeaveAvailed = "2 TIMES",
        NsgRankId = 3,
        NsgRank = new RankHierarchy { Id = 3, RankName = "Inspector" },
        NsgTradeId = 7,
        NsgTrade = new NsgTrade { Id = 7, NsgTradeName = "GD" },
        ParentDepartmentRankId = 5,
        ParentDepartmentRank = new ParentDepartmentRank { Id = 5, Name = "Havildar" },
        PdTradeId = 9,
        PdTrade = new PdTrade { Id = 9, PdTradeName = "Signal" },
        NsgUnitId = 4,
        NsgUnit = new NsgUnit { Id = 4, Name = "Unit Four" },
        SideSteppedFromNsgUnitId = 6,
        SideSteppedFromNsgUnit = new NsgUnit { Id = 6, Name = "Unit Six" },
        EducationId = 2,
        Education = new EducationQualification { Id = 2, CourseName = "BA" },
        ReligionId = 1,
        Religion = new Religion { Id = 1, Name = "HINDU" },
        FatherName = "Ram Saran",
        MotherName = "Savitri Devi",
        SpouseName = "Laxmi",
        NextOfKinMobileNumber = "9876543210",
        NumberOfChildren = 2,
        Category = PersonnelCategory.GEN,
        BloodGroup = BloodGroupType.OPositive,
        HeightCm = 172,
        WeightKg = 70.5m,
        PermanentVillage = "Rampur",
        PermanentPostOffice = "Rampur PO",
        PermanentPoliceStation = "Rampur PS",
        PermanentDistrict = "Muzaffarnagar",
        PermanentStateId = 10,
        PermanentState = new State { Id = 10, Name = "Uttar Pradesh" },
        PermanentPinCode = "251001",
        AadhaarNumberEncrypted = Cipher,
        PanNumberEncrypted = Cipher,
        PranNumberEncrypted = Cipher,
        SalaryAccountNumberEncrypted = Cipher,
        PanNumberHash = Cipher,
        SalaryAccountNumberHash = Cipher
    };

    private static (MobileProfileService Service, FakeCurrentUser User, FakeProfileRepository Profiles, FakePisRepository PisRepo) Build()
    {
        FakeProfileRepository profiles = new(new FakeGrantRepository(new TestClock()));
        FakePisRepository pis = new();
        FakeCurrentUser user = new();
        return (new MobileProfileService(user, profiles, pis), user, profiles, pis);
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
        Assert.Equal("Inspector", r.Data.NsgRankName);
        Assert.Equal("Unit Four", r.Data.NsgUnitName);
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

        Assert.Equal(3, dto.NsgRankId); Assert.Equal("Inspector", dto.NsgRankName);
        Assert.Equal(7, dto.NsgTradeId); Assert.Equal("GD", dto.NsgTradeName);
        Assert.Equal(5, dto.ParentDepartmentRankId); Assert.Equal("Havildar", dto.ParentDepartmentRankName);
        Assert.Equal(9, dto.PdTradeId); Assert.Equal("Signal", dto.PdTradeName);
        Assert.Equal(4, dto.NsgUnitId); Assert.Equal("Unit Four", dto.NsgUnitName);
        Assert.Equal(6, dto.SideSteppedFromNsgUnitId); Assert.Equal("Unit Six", dto.SideSteppedFromNsgUnitName);
        Assert.Equal(2, dto.EducationId); Assert.Equal("BA", dto.EducationName);
        Assert.Equal(1, dto.ReligionId); Assert.Equal("HINDU", dto.ReligionName);
        Assert.Equal(10, dto.PermanentStateId); Assert.Equal("Uttar Pradesh", dto.PermanentStateName);
    }

    [Fact]
    public async Task All_personal_and_family_fields_are_mapped()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE" });
        user.ProfileId = 9;

        MobileProfileResponseDto dto = (await service.GetMyProfileAsync()).Data!;

        Assert.Equal(PersonnelCategory.GEN, dto.Category);
        Assert.Equal("Ram Saran", dto.FatherName); Assert.Equal("Savitri Devi", dto.MotherName);
        Assert.Equal("Laxmi", dto.SpouseName); Assert.Equal("9876543210", dto.NextOfKinMobileNumber);
        Assert.Equal(2, dto.NumberOfChildren);
        Assert.Equal(BloodGroupType.OPositive, dto.BloodGroup);
        Assert.Equal(172, dto.HeightCm); Assert.Equal(70.5m, dto.WeightKg);
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
        Assert.Equal(new DateOnly(2012, 7, 1), dto.SideSteppedOn);
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

        Assert.Equal(12.5m, dto.EarnedLeaveDue); Assert.Equal(4.0m, dto.HalfPayLeaveDue);
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
        Assert.Null(dto.NsgRankId); Assert.Null(dto.NsgRankName);
        Assert.Null(dto.NsgTradeId); Assert.Null(dto.NsgTradeName);
        Assert.Null(dto.ParentDepartmentRankId); Assert.Null(dto.ParentDepartmentRankName);
        Assert.Null(dto.PdTradeId); Assert.Null(dto.PdTradeName);
        Assert.Null(dto.NsgUnitId); Assert.Null(dto.NsgUnitName);
        Assert.Null(dto.SideSteppedFromNsgUnitId); Assert.Null(dto.SideSteppedFromNsgUnitName);
        Assert.Null(dto.EducationId); Assert.Null(dto.EducationName);
        Assert.Null(dto.ReligionId); Assert.Null(dto.ReligionName);
        Assert.Null(dto.PermanentStateId); Assert.Null(dto.PermanentStateName);
        Assert.Null(dto.DateOfBirth); Assert.Null(dto.Category);
        Assert.Null(dto.BloodGroup); Assert.Null(dto.HeightCm); Assert.Null(dto.WeightKg);
    }

    [Fact]
    public async Task The_response_never_carries_sensitive_fields()
    {
        var (service, user, profiles, pis) = Build();
        pis.Rows.Add(Pis(1, "NSG-MINE", "My Name", "9000000002"));
        profiles.Profiles.Add(new MobileUserProfile { Id = 9, NsgNumber = "NSG-MINE", MpinHash = "MPIN-HASH-VALUE", PasswordHash = "PW-HASH-VALUE" });
        user.ProfileId = 9;

        string json = JsonSerializer.Serialize((await service.GetMyProfileAsync()).Data);

        Assert.DoesNotContain(Cipher, json);
        Assert.DoesNotContain("HASH-VALUE", json);
        foreach (PropertyInfo p in typeof(MobileProfileResponseDto).GetProperties())
            Assert.DoesNotMatch("(?i)aadhaar|pan|pran|salary|hash|mpin|password|encrypted|isactive|mobilestatus|createdat|updatedat|createdby|updatedby", p.Name);
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
}
