using System.Reflection;
using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Profile.DTOs;
using NSG.Mobile.Application.Profile.Services;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Tests;

/// <summary>GET /api/v1/profile logic over in-memory fakes: identity from the token only, NSG-number link, minimal DTO.</summary>
public class MobileProfileServiceTests
{
    private const string Cipher = "CIPHER-SECRET-VALUE";

    private static UserPis Pis(long id, string nsg, string name, string mobile) => new()
    {
        Id = id, NsgNumber = nsg, ServiceNumber = "SVC-" + nsg, FullName = name, MobileNumber = mobile,
        DateOfBirth = new DateOnly(1990, 5, 15),
        NsgRank = new RankHierarchy { Id = 3, RankName = "Inspector" }, NsgUnit = new NsgUnit { Id = 4, Name = "Unit Four" },
        AadhaarNumberEncrypted = Cipher, PanNumberEncrypted = Cipher, PranNumberEncrypted = Cipher,
        SalaryAccountNumberEncrypted = Cipher, PanNumberHash = Cipher, SalaryAccountNumberHash = Cipher
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
        Assert.Equal("Inspector", r.Data.RankName);
        Assert.Equal("Unit Four", r.Data.NsgUnitName);
        Assert.Equal(new DateOnly(1990, 5, 15), r.Data.DateOfBirth);
        Assert.Equal("9000000002", r.Data.MobileNumber);
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
            Assert.DoesNotMatch("(?i)aadhaar|pan|pran|salary|hash|mpin|password|encrypted", p.Name);
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
