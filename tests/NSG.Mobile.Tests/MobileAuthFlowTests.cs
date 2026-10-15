using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// End-to-end registration → approval → MPIN → login → change → reset over HTTP against real MySQL, in demo-OTP mode
/// (the test host runs in Development, where Otp:Mode=Demo). Needs NSG_TEST_MYSQL; see MobileAuthDatabaseTests.
/// </summary>
[Collection("mysql")]
public class MobileAuthFlowTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/mobile-auth";
    private const string Approvals = "/api/v1/mobile-approval-requests";
    private const string Nsg = "FLOW-1001", Mobile = "9812345670", Dob = "1991-03-04", DemoOtp = "123456";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous, _admin;

    public MobileAuthFlowTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymous = _factory.CreateClientFor(null);
        _admin = _factory.CreateClientFor(DomainConstants.Roles.ITAdmin);
    }

    public async Task InitializeAsync() => await CleanAsync();
    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `approve_mobile_users`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_auth_grants`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private async Task SeedPisAsync(MobileAppActivationStatus status = MobileAppActivationStatus.Pending)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = Nsg, ServiceNumber = "S", FullName = "Flow Person", MobileNumber = Mobile, DateOfBirth = DateOnly.Parse(Dob), MobileAppActivationStatus = status });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Moves stored OTPs into the past so the resend cooldown and hourly cap do not interfere with a multi-step test.</summary>
    private async Task AgeOtpsAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE `otp_requests` SET `created_at_ist` = `created_at_ist` - INTERVAL 3 HOUR");
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, object body)
    {
        HttpResponseMessage response = await client.PostAsync(url, Json(body));
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"{url} returned a blank body ({(int)response.StatusCode}).");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private object Identity => new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob };
    private static string Status(JsonElement body) => body.GetProperty("data").GetProperty("status").GetString()!;

    private async Task<(HttpStatusCode, JsonElement)> SendOtpAsync(string flow = "Registration")
        => await PostAsync(_anonymous, $"{Auth}/otp/send", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow });

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAndVerifyAsync(string flow = "Registration", string code = DemoOtp)
    {
        (HttpStatusCode sendStatus, JsonElement sent) = await SendOtpAsync(flow);
        Assert.Equal(HttpStatusCode.OK, sendStatus);
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        return await PostAsync(_anonymous, $"{Auth}/otp/verify", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow, otpReferenceId = reference, otpCode = code });
    }

    [Fact]
    public async Task Demo_otp_send_says_demo_mode_and_never_claims_an_sms_was_delivered()
    {
        await SeedPisAsync();
        (HttpStatusCode status, JsonElement body) = await SendOtpAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("OTP_SENT", Status(body));
        Assert.True(body.GetProperty("data").GetProperty("demoOtpMode").GetBoolean());
        string message = body.GetProperty("message").GetString()!;
        Assert.StartsWith("Demo OTP mode is active", message);
        Assert.DoesNotContain("has been sent to your", message);
        Assert.DoesNotContain(DemoOtp, JsonSerializer.Serialize(body));   // the code itself is never echoed

        await using AppDbContext ctx = _factory.CreateContext();
        OtpRequest stored = await ctx.OtpRequests.SingleAsync();
        Assert.NotEqual(DemoOtp, stored.OtpHash);   // hashed, not plaintext
    }

    [Fact]
    public async Task Wrong_demo_code_is_refused_the_attempt_limit_applies_and_the_right_code_is_single_use()
    {
        await SeedPisAsync();
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAndVerifyOnSameReferenceAsync("000000", i == 0)).Status);

        // Attempts exhausted: even 123456 is refused now.
        HttpStatusCode afterLimit = (await SendAndVerifyOnSameReferenceAsync(DemoOtp, false)).Status;
        Assert.Equal(HttpStatusCode.TooManyRequests, afterLimit);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Empty(await ctx.ApproveMobileUsers.ToListAsync());
    }

    private Guid? _reference;
    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAndVerifyOnSameReferenceAsync(string code, bool sendFirst)
    {
        if (sendFirst)
        {
            (_, JsonElement sent) = await SendOtpAsync();
            _reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        }
        return await PostAsync(_anonymous, $"{Auth}/otp/verify", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow = "Registration", otpReferenceId = _reference, otpCode = code });
    }

    [Fact]
    public async Task Resend_cooldown_applies_in_demo_mode()
    {
        await SeedPisAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendOtpAsync()).Item1);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendOtpAsync()).Item1);
    }

    [Fact]
    public async Task Pending_person_registers_is_approved_then_completes_mpin_login_change_and_reset()
    {
        await SeedPisAsync();

        // 1. New person: OTP required, then OTP verification raises exactly one approval request.
        Assert.Equal("OTP_REQUIRED", Status((await PostAsync(_anonymous, $"{Auth}/login/check", Identity)).Body));
        (HttpStatusCode raisedStatus, JsonElement raised) = await SendAndVerifyAsync();
        Assert.Equal(HttpStatusCode.Created, raisedStatus);
        Assert.Equal("Your approval request has been raised successfully.", raised.GetProperty("message").GetString());

        // 2. Pending: PIS is not consulted, no OTP is sent, no second request.
        int otpsBefore;
        await using (AppDbContext ctx = _factory.CreateContext()) otpsBefore = await ctx.OtpRequests.CountAsync();
        (HttpStatusCode _, JsonElement pending) = await PostAsync(_anonymous, $"{Auth}/login/check", Identity);
        Assert.Equal("REQUEST_PENDING", Status(pending));
        Assert.Equal("Your request is already pending. Please wait for approval.", pending.GetProperty("message").GetString());
        (HttpStatusCode _, JsonElement pendingSend) = await SendOtpAsync();
        Assert.Equal("REQUEST_PENDING", Status(pendingSend));
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            Assert.Equal(otpsBefore, await ctx.OtpRequests.CountAsync());
            Assert.Equal(1, await ctx.ApproveMobileUsers.CountAsync());
        }

        // 3. IT Admin approves.
        long requestId;
        await using (AppDbContext ctx = _factory.CreateContext()) requestId = (await ctx.ApproveMobileUsers.SingleAsync()).Id;
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsync($"{Approvals}/{requestId}/decision", Json(new { decision = "Approved" }))).StatusCode);

        // 4. Approved: OTP → registration grant → submit → MPIN-setup grant → profile. No second approval request.
        await AgeOtpsAsync();
        (HttpStatusCode _, JsonElement verified) = await SendAndVerifyAsync();
        Assert.Equal("REGISTRATION_VERIFIED", Status(verified));
        string registrationGrant = verified.GetProperty("data").GetProperty("grantToken").GetString()!;

        // The registration grant is not an access token.
        HttpClient asBearer = _factory.CreateClientFor(null);
        asBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registrationGrant);
        Assert.Equal(HttpStatusCode.Unauthorized, (await asBearer.PostAsync($"{Auth}/mpin/change", Json(new { currentMpin = "2580", newMpin = "1357", confirmNewMpin = "1357" }))).StatusCode);

        (HttpStatusCode _, JsonElement submitted) = await PostAsync(_anonymous, $"{Auth}/registration/submit", new { grantToken = registrationGrant });
        Assert.Equal("MPIN_SETUP_GRANTED", Status(submitted));
        Assert.Equal("GRANT_INVALID", (await PostAsync(_anonymous, $"{Auth}/registration/submit", new { grantToken = registrationGrant })).Body.GetProperty("title").GetString());

        string setupGrant = submitted.GetProperty("data").GetProperty("grantToken").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(_anonymous, $"{Auth}/mpin/setup", new { grantToken = setupGrant, mpin = "1234", confirmMpin = "1234" })).Status);   // too easy
        (HttpStatusCode setupStatus, JsonElement setup) = await PostAsync(_anonymous, $"{Auth}/mpin/setup", new { grantToken = setupGrant, mpin = "2580", confirmMpin = "2580", role = "SystemAdmin" });
        Assert.Equal(HttpStatusCode.Created, setupStatus);
        Assert.Equal("PROFILE_CREATED", Status(setup));
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            MobileUserProfile profile = await ctx.MobileUserProfiles.SingleAsync();
            Assert.Equal(UserRole.Employee, profile.Role);                  // the client-supplied role was ignored
            Assert.NotEqual("2580", profile.MpinHash);
            Assert.StartsWith("100000.", profile.MpinHash);                 // PBKDF2 100K, salted
            Assert.Equal(1, await ctx.ApproveMobileUsers.CountAsync());     // still only the one, historical request
        }

        // 5. Login: wrong MPIN → no token; right MPIN → JWT; MPIN required at check.
        Assert.Equal("MPIN_REQUIRED", Status((await PostAsync(_anonymous, $"{Auth}/login/check", Identity)).Body));
        (HttpStatusCode wrongStatus, JsonElement wrong) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongStatus);
        Assert.False(wrong.TryGetProperty("data", out _));
        (HttpStatusCode loginStatus, JsonElement login) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = "2580" });
        Assert.Equal(HttpStatusCode.OK, loginStatus);
        string accessToken = login.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.Equal("Employee", login.GetProperty("data").GetProperty("profile").GetProperty("role").GetString());

        // 6. The mobile token can change the MPIN but cannot reach the admin API.
        HttpClient mobile = _factory.CreateClientFor(null);
        mobile.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await mobile.GetAsync(Approvals)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(mobile, $"{Auth}/mpin/change", new { currentMpin = "2580", newMpin = "2580", confirmNewMpin = "2580" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(mobile, $"{Auth}/mpin/change", new { currentMpin = "9999", newMpin = "1357", confirmNewMpin = "1357" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(mobile, $"{Auth}/mpin/change", new { currentMpin = "2580", newMpin = "1357", confirmNewMpin = "1357" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = "1357" })).Status);

        // 7. Forgotten MPIN: identifiers alone do nothing; OTP → reset grant → reset.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(_anonymous, $"{Auth}/mpin/reset", new { grantToken = "invented-grant", newMpin = "2468", confirmNewMpin = "2468" })).Status);
        await AgeOtpsAsync();
        (HttpStatusCode _, JsonElement resetVerified) = await SendAndVerifyAsync("MpinReset");
        Assert.Equal("MPIN_RESET_GRANTED", Status(resetVerified));
        string resetGrant = resetVerified.GetProperty("data").GetProperty("grantToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/mpin/reset", new { grantToken = resetGrant, newMpin = "2468", confirmNewMpin = "2468" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(_anonymous, $"{Auth}/mpin/reset", new { grantToken = resetGrant, newMpin = "8642", confirmNewMpin = "8642" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = "2468" })).Status);
    }

    [Fact]
    public async Task Five_wrong_mpins_lock_the_profile_and_a_correct_mpin_is_then_refused_with_423()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.MobileUserProfiles.Add(new MobileUserProfile
            {
                NsgNumber = Nsg, MobileNumber = Mobile, DateOfBirth = DateOnly.Parse(Dob), Role = UserRole.Employee,
                MpinHash = new NSG.Mobile.Infrastructure.Security.PasswordHashingService().HashPassword("2580")
            });
            await ctx.SaveChangesAsync();
        }

        object Login(string mpin) => new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin };
        for (int i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/login", Login("0000"))).Status);
        Assert.Equal((HttpStatusCode)423, (await PostAsync(_anonymous, $"{Auth}/login", Login("0000"))).Status);
        Assert.Equal((HttpStatusCode)423, (await PostAsync(_anonymous, $"{Auth}/login", Login("2580"))).Status);

        await using AppDbContext check = _factory.CreateContext();
        MobileUserProfile locked = await check.MobileUserProfiles.SingleAsync();
        Assert.True(locked.LockedUntilIst > IndianStandardTime.Now);
        Assert.Equal(5, locked.FailedLoginAttempts);
    }

    [Fact]
    public async Task Concurrent_otp_verifications_raise_a_single_approval_request()
    {
        await SeedPisAsync();
        (_, JsonElement sent) = await SendOtpAsync();
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        object body = new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow = "Registration", otpReferenceId = reference, otpCode = DemoOtp };

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(_anonymous, $"{Auth}/otp/verify", body)));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.ApproveMobileUsers.CountAsync(a => a.ApprovalStatus == MobileAppActivationStatus.Pending));
        Assert.Equal(1, await ctx.ApproveMobileUsers.CountAsync());
    }

    [Fact]
    public async Task Rejected_person_is_blocked_with_the_it_support_message_and_gets_no_otp()
    {
        await SeedPisAsync(MobileAppActivationStatus.Rejected);
        (HttpStatusCode _, JsonElement check) = await PostAsync(_anonymous, $"{Auth}/login/check", Identity);
        Assert.Equal("ACCESS_UNAVAILABLE", Status(check));
        Assert.Equal("Your access is unavailable. Please contact the IT Admin.", check.GetProperty("message").GetString());
        (HttpStatusCode _, JsonElement send) = await SendOtpAsync();
        Assert.Equal("ACCESS_UNAVAILABLE", Status(send));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(0, await ctx.OtpRequests.CountAsync());
        Assert.Equal(0, await ctx.ApproveMobileUsers.CountAsync());
    }
}
