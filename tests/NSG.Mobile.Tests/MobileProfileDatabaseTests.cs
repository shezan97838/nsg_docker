using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL tests of GET /api/v1/profile over HTTP with real mobile tokens: success, missing / invalid token, a revoked session,
/// a missing PIS record, a staff token, and attempts to select another person. Needs NSG_TEST_MYSQL (a database named *_test).
/// </summary>
[Collection("mysql")]
public class MobileProfileDatabaseTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/mobile-auth";
    private const string ProfileUrl = "/api/v1/profile";
    private const string Nsg = "PROF-1001", OtherNsg = "PROF-2002", Mobile = "9823456711", OtherMobile = "9823456722", Dob = "1992-07-08", Mpin = "2580";
    private const string Aadhaar = "123546789532", Pan = "AGIPR3625F", Pran = "321654987321", Account = "30927989329";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public MobileProfileDatabaseTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(NewPis(Nsg, "Profile One", Mobile));
        ctx.UsersPis.Add(NewPis(OtherNsg, "Profile Two", OtherMobile));
        foreach ((string nsg, string mobile) in new[] { (Nsg, Mobile), (OtherNsg, OtherMobile) })
            ctx.MobileUserProfiles.Add(new MobileUserProfile
            {
                NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = DateOnly.Parse(Dob), Role = UserRole.Employee,
                MpinHash = new PasswordHashingService().HashPassword(Mpin)
            });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await CleanAsync();

    // Encrypted with the API host's own IEncryptionService, so the endpoint decrypts with the same key.
    private UserPis NewPis(string nsg, string name, string mobile)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IEncryptionService encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        return new UserPis
        {
            NsgNumber = nsg, ServiceNumber = "SVC-" + nsg, FullName = name, MobileNumber = mobile, DateOfBirth = DateOnly.Parse(Dob),
            Gender = Gender.Female,
            AadhaarNumberEncrypted = encryption.Encrypt(Aadhaar), PanNumberEncrypted = encryption.Encrypt(Pan),
            PranNumberEncrypted = encryption.Encrypt(Pran), SalaryAccountNumberEncrypted = encryption.Encrypt(Account)
        };
    }

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_auth_grants`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(string Access, string Refresh)> LoginAsync(string nsg = Nsg, string mobile = Mobile)
    {
        HttpResponseMessage response = await _anonymous.PostAsync($"{Auth}/login", Json(new { nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob, mpin = Mpin }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement data = (await ReadAsync(response)).GetProperty("data");
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    private HttpClient Bearer(string token)
    {
        HttpClient client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_valid_mobile_token_returns_only_the_callers_basic_pis_details()
    {
        (string access, _) = await LoginAsync();

        HttpResponseMessage response = await Bearer(access).GetAsync(ProfileUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(text).RootElement;
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("Personnel details fetched successfully", body.GetProperty("message").GetString());
        JsonElement data = body.GetProperty("data");
        Assert.Equal(Nsg, data.GetProperty("nsgNumber").GetString());
        Assert.Equal("Profile One", data.GetProperty("fullName").GetString());
        Assert.Equal("1992-07-08", data.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Female", data.GetProperty("gender").GetString());
        Assert.Equal(Aadhaar, data.GetProperty("aadhaarNumber").GetString());
        Assert.Equal(Pan, data.GetProperty("panNumber").GetString());
        Assert.Equal(Pran, data.GetProperty("pranNumber").GetString());
        Assert.Equal(Account, data.GetProperty("salaryAccountNumber").GetString());
        Assert.DoesNotContain(OtherNsg, text);
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            UserPis stored = await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == Nsg);
            foreach (string hiddenValue in new[] { stored.AadhaarNumberEncrypted!, stored.PanNumberEncrypted!, stored.PranNumberEncrypted!, stored.SalaryAccountNumberEncrypted! })
                Assert.DoesNotContain(hiddenValue, text);   // ciphertext is never returned; the stored values are untouched
        }
        foreach (string forbidden in new[] { "hash", "mpin", "password", "encrypted" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_client_supplied_nsg_number_or_id_cannot_select_another_persons_profile()
    {
        (string access, _) = await LoginAsync();
        HttpClient client = Bearer(access);

        foreach (string url in new[] { $"{ProfileUrl}?nsgNumber={OtherNsg}", $"{ProfileUrl}?id=2&profileId=2&userId=2" })
        {
            HttpResponseMessage response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Nsg, (await ReadAsync(response)).GetProperty("data").GetProperty("nsgNumber").GetString());
        }

        client.DefaultRequestHeaders.Add("X-Nsg-Number", OtherNsg);
        Assert.Equal(Nsg, (await ReadAsync(await client.GetAsync(ProfileUrl))).GetProperty("data").GetProperty("nsgNumber").GetString());
    }

    [Fact]
    public async Task Each_mobile_user_sees_only_their_own_record()
    {
        (string first, _) = await LoginAsync();
        (string second, _) = await LoginAsync(OtherNsg, OtherMobile);

        Assert.Equal("Profile One", (await ReadAsync(await Bearer(first).GetAsync(ProfileUrl))).GetProperty("data").GetProperty("fullName").GetString());
        Assert.Equal("Profile Two", (await ReadAsync(await Bearer(second).GetAsync(ProfileUrl))).GetProperty("data").GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task A_missing_or_invalid_token_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(ProfileUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer("not.a.jwt").GetAsync(ProfileUrl)).StatusCode);
        (_, string refresh) = await LoginAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(refresh).GetAsync(ProfileUrl)).StatusCode);   // a refresh token is not a bearer token
    }

    [Fact]
    public async Task A_logged_out_session_is_401_at_once()
    {
        (string access, string refresh) = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await Bearer(access).GetAsync(ProfileUrl)).StatusCode);

        await _anonymous.PostAsync($"{Auth}/logout", Json(new { refreshToken = refresh }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(access).GetAsync(ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_profile_loses_access_immediately()
    {
        (string access, _) = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_user_profiles` SET `is_active` = 0");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(access).GetAsync(ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task A_missing_pis_record_is_404_with_a_message()
    {
        (string access, _) = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis` WHERE `nsg_number` = {0}", Nsg);

        HttpResponseMessage response = await Bearer(access).GetAsync(ProfileUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Your personnel record was not found.", (await ReadAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_staff_token_is_not_a_mobile_identity()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClientFor(DomainConstants.Roles.PisAdmin).GetAsync(ProfileUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClientFor("Employee").GetAsync(ProfileUrl)).StatusCode);
    }
}
