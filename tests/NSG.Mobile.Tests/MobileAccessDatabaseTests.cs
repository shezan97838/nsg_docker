using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL tests, over HTTP and the real authentication pipeline, of the PIS Admin "Block / Restore Mobile Access" feature and of
/// <c>SURAKSHA_1001</c>: the admin endpoints and their authorization, the revocation of every session, what the blocked person's
/// access token, refresh token, login and login/check receive, restore, the cases that must never return the code, and the races
/// between a block and a login or a refresh. Needs NSG_TEST_MYSQL (a database whose name ends in "_test").
/// </summary>
[Collection("mysql")]
public class MobileAccessDatabaseTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/mobile-auth";
    private const string Admin = "/api/v1/admin/mobile-access";
    private const string Nsg = "ACC-1001", Mobile = "9823456799", Dob = "1992-07-08", Mpin = "2580";
    private const string BlockCode = "SURAKSHA_1001";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;
    private HttpClient _pisAdmin = null!;
    private HttpClient _superAdmin = null!;

    public MobileAccessDatabaseTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync()
    {
        (_pisAdmin, _superAdmin) = await PisAdminSession.CreateClientsAsync(_factory);   // clears the admin tables, audit logs and OTPs
        await CleanMobileAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(NewPis(Nsg, Mobile));
        ctx.MobileUserProfiles.Add(new MobileUserProfile
        {
            NsgNumber = Nsg, MobileNumber = Mobile, DateOfBirth = DateOnly.Parse(Dob), Role = UserRole.Employee,
            MpinHash = new PasswordHashingService().HashPassword(Mpin)
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanMobileAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static UserPis NewPis(string nsg, string mobile) => new()
    {
        NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = "Access Person", NsgRank = "Inspector", NsgUnit = "Unit One",
        MobileNumber = mobile, DateOfBirth = DateOnly.Parse(Dob), IsActive = true, MobileAppActivationStatus = MobileAppActivationStatus.Approved
    };

    private async Task CleanMobileAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_auth_grants`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis` WHERE `nsg_number` NOT LIKE 'PAS-%'");
    }

    // ───── helpers ─────

    private static StringContent Json(object? body) => new(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"A blank body ({(int)response.StatusCode}).");
        JsonElement body = JsonDocument.Parse(text).RootElement.Clone();
        Assert.True(body.TryGetProperty("success", out _) && body.TryGetProperty("message", out _) && body.TryGetProperty("data", out _), text);
        return (response.StatusCode, body);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, object? body = null)
        => await ReadAsync(await client.PostAsync(url, Json(body)));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(HttpClient client, string url, object? body = null)
        => await ReadAsync(await client.PutAsync(url, Json(body)));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient client, string url)
        => await ReadAsync(await client.GetAsync(url));

    private record Tokens(string Access, string Refresh);

    private Task<(HttpStatusCode Status, JsonElement Body)> LoginRawAsync(string mpin = Mpin)
        => PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin });

    private async Task<Tokens> LoginAsync(string mpin = Mpin)
    {
        (HttpStatusCode status, JsonElement body) = await LoginRawAsync(mpin);
        Assert.Equal(HttpStatusCode.OK, status);
        return ReadTokens(body);
    }

    private static Tokens ReadTokens(JsonElement body)
        => new(body.GetProperty("data").GetProperty("accessToken").GetString()!, body.GetProperty("data").GetProperty("refreshToken").GetString()!);

    private HttpClient Bearer(string accessToken)
    {
        HttpClient client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(string refreshToken) => PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken });

    /// <summary>
    /// An authenticated call: mpin/change with a wrong current MPIN. A live session reaches the service, which answers 401 "Current MPIN
    /// is incorrect." (and counts a failed attempt, so keep the number of probes of one profile below the lockout limit); a dead one is
    /// refused before it. Probing is therefore read as: alive = reached the service.
    /// </summary>
    private async Task<(bool Alive, HttpStatusCode Status, JsonElement Body)> ProbeAsync(string accessToken)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(Bearer(accessToken), $"{Auth}/mpin/change", new { currentMpin = "9999", newMpin = "1357", confirmNewMpin = "1357" });
        bool alive = status == HttpStatusCode.Unauthorized && body.GetProperty("message").GetString() == "Current MPIN is incorrect.";
        return (alive, status, body);
    }

    private static void AssertBlockCode(JsonElement body)
    {
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(BlockCode, body.GetProperty("code").GetString());
        Assert.Equal("Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    private static void AssertNoCode(JsonElement body, string because = "")
        => Assert.False(body.TryGetProperty("code", out JsonElement code), $"No public code (SURAKSHA_1001 / SURAKSHA_1002) may be returned here {because}: {code}");

    private async Task<MobileUserProfile> ProfileAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.MobileUserProfiles.AsNoTracking().SingleAsync(p => p.NsgNumber == Nsg);
    }

    private async Task<List<MobileRefreshToken>> TokensAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.MobileRefreshTokens.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
    }

    private async Task ResetProfileAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE `mobile_user_profiles` SET `is_active` = 1, `access_blocked_at_ist` = NULL, `access_blocked_by` = NULL, `access_block_reason` = NULL, `failed_login_attempts` = 0, `locked_until_ist` = NULL");
    }

    // ───── authorization ─────

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "/block")]
    [InlineData("PUT", "/restore")]
    public async Task Only_a_PIS_admin_portal_token_can_call_the_endpoints(string method, string suffix)
    {
        string url = $"{Admin}/{Nsg}{suffix}";
        Tokens mobile = await LoginAsync();
        HttpClient staffPisAdmin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);   // a staff Bearer token with the PisAdmin role
        HttpMethod verb = new(method);

        async Task<HttpStatusCode> Send(HttpClient client)
        {
            using HttpRequestMessage request = new(verb, url);
            if (verb != HttpMethod.Get) request.Content = Json(new { reason = "x" });
            return (await client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Send(_anonymous));
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(Bearer(mobile.Access)));      // a mobile user
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(staffPisAdmin));              // the staff path RequirePisAdmin admits is not admitted here
        Assert.Equal(HttpStatusCode.Forbidden, await Send(_superAdmin));                   // another admin role
        Assert.Equal(HttpStatusCode.OK, await Send(_pisAdmin));

        // None of the refused calls changed anything.
        MobileUserProfile profile = await ProfileAsync();
        Assert.True(profile.IsActive || method == "PUT" && suffix == "/block");            // only the permitted block may have run
        if (suffix != "/block") Assert.Null(profile.AccessBlockedAtIst);
    }

    // ───── lookup ─────

    [Fact]
    public async Task Lookup_covers_pis_with_profile_pis_without_profile_and_unknown()
    {
        await LoginAsync();

        // existing PIS record + mobile profile
        (HttpStatusCode status, JsonElement body) = await GetAsync(_pisAdmin, $"{Admin}/{Nsg}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.TryGetProperty("code", out _));
        JsonElement data = body.GetProperty("data");
        Assert.Equal(Nsg, data.GetProperty("nsgNumber").GetString());
        Assert.Equal("Access Person", data.GetProperty("fullName").GetString());
        Assert.Equal("Inspector", data.GetProperty("rank").GetString());
        Assert.Equal("Unit One", data.GetProperty("unit").GetString());
        Assert.Equal("XXXXXX6799", data.GetProperty("maskedMobileNumber").GetString());
        Assert.True(data.GetProperty("pisRecordExists").GetBoolean());
        Assert.True(data.GetProperty("mobileProfileExists").GetBoolean());
        Assert.Equal("ACTIVE", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.True(data.GetProperty("hasActiveSession").GetBoolean());
        Assert.False(data.GetProperty("isLockedOut").GetBoolean());
        Assert.EndsWith("+05:30", data.GetProperty("lastLoginAt").GetString());
        string raw = data.GetRawText();
        foreach (string forbidden in new[] { "mpin", "hash", "dateOfBirth", "aadhaar", "pan", "pran", "salary", Mobile })
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);

        // PIS record without a mobile profile
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.Add(NewPis("ACC-2002", "9823456798"));
            await ctx.SaveChangesAsync();
        }
        (HttpStatusCode noProfileStatus, JsonElement noProfile) = await GetAsync(_pisAdmin, $"{Admin}/ACC-2002");
        Assert.Equal(HttpStatusCode.OK, noProfileStatus);
        Assert.Equal("Personnel record found, but no mobile profile has been created for this NSG number.", noProfile.GetProperty("message").GetString());
        Assert.False(noProfile.GetProperty("data").GetProperty("mobileProfileExists").GetBoolean());
        Assert.Equal("NO_MOBILE_PROFILE", noProfile.GetProperty("data").GetProperty("status").GetString());
        (HttpStatusCode blockStatus, JsonElement blockBody) = await PutAsync(_pisAdmin, $"{Admin}/ACC-2002/block");
        Assert.Equal(HttpStatusCode.NotFound, blockStatus);
        Assert.Equal("A personnel record exists for this NSG number, but the person has not registered in the mobile app, so there is no mobile access to change.", blockBody.GetProperty("message").GetString());

        // unknown, and over-long
        (HttpStatusCode unknownStatus, JsonElement unknown) = await GetAsync(_pisAdmin, $"{Admin}/NOPE-0");
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Equal("No personnel record or mobile profile was found for this NSG number.", unknown.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(_pisAdmin, $"{Admin}/{new string('N', 31)}")).Status);
    }

    // ───── block ─────

    [Fact]
    public async Task Block_ends_every_session_at_once_and_the_person_gets_the_code_on_every_path()
    {
        Tokens session = await LoginAsync();
        { var probe = await ProbeAsync(session.Access); Assert.True(probe.Alive, $"{(int)probe.Status} {probe.Body}"); }

        (HttpStatusCode status, JsonElement body) = await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = "  device reported lost  " });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Mobile access blocked successfully. All active sessions have been terminated.", body.GetProperty("message").GetString());
        Assert.False(body.TryGetProperty("code", out _));                               // the admin's own response is an ordinary one
        Assert.Equal("ADMIN_BLOCKED", body.GetProperty("data").GetProperty("status").GetString());
        Assert.False(body.GetProperty("data").GetProperty("hasActiveSession").GetBoolean());
        Assert.Equal("device reported lost", body.GetProperty("data").GetProperty("accessBlockReason").GetString());

        MobileUserProfile profile = await ProfileAsync();
        Assert.False(profile.IsActive);
        Assert.NotNull(profile.AccessBlockedAtIst);
        Assert.InRange((NSG.Mobile.Domain.Common.IndianStandardTime.Now - profile.AccessBlockedAtIst!.Value).TotalSeconds, -5, 60);   // IST "now"
        Assert.True(profile.AccessBlockedBy > 0);
        Assert.Equal("device reported lost", profile.AccessBlockReason);
        List<MobileRefreshToken> tokens = await TokensAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, t => { Assert.NotNull(t.RevokedAtIst); Assert.Equal(MobileSessionRevokeReasons.AdminBlocked, t.RevokeReason); });

        // 1. the existing access token, on its very next authenticated request
        (bool alive, HttpStatusCode accessStatus, JsonElement accessBody) = await ProbeAsync(session.Access);
        Assert.False(alive);
        Assert.Equal(HttpStatusCode.Unauthorized, accessStatus);
        AssertBlockCode(accessBody);

        // 2. the refresh token: no new tokens
        (HttpStatusCode refreshStatus, JsonElement refreshBody) = await RefreshAsync(session.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, refreshStatus);
        AssertBlockCode(refreshBody);

        // 3. login with the right MPIN: no tokens, no session, no failed attempt, block untouched
        int rows = (await TokensAsync()).Count;
        int attemptsBefore = (await ProfileAsync()).FailedLoginAttempts;        // the live-session probe above was itself a wrong-MPIN call
        (HttpStatusCode loginStatus, JsonElement loginBody) = await LoginRawAsync();
        Assert.Equal(HttpStatusCode.Forbidden, loginStatus);
        AssertBlockCode(loginBody);
        Assert.Equal(JsonValueKind.Null, loginBody.GetProperty("data").ValueKind);            // no tokens in the response
        Assert.Equal(rows, (await TokensAsync()).Count);
        MobileUserProfile after = await ProfileAsync();
        Assert.Equal(attemptsBefore, after.FailedLoginAttempts);                 // a valid MPIN on a blocked profile is not a failed attempt
        Assert.Equal(profile.AccessBlockedAtIst, after.AccessBlockedAtIst);

        // 4. login/check: the usual unavailable status, plus the code
        (HttpStatusCode checkStatus, JsonElement check) = await PostAsync(_anonymous, $"{Auth}/login/check", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob });
        Assert.Equal(HttpStatusCode.OK, checkStatus);
        Assert.Equal("ACCESS_UNAVAILABLE", check.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(BlockCode, check.GetProperty("code").GetString());
        Assert.True(check.GetProperty("success").GetBoolean());

        // the lookup shows the block
        JsonElement look = (await GetAsync(_pisAdmin, $"{Admin}/{Nsg}")).Body.GetProperty("data");
        Assert.Equal("ADMIN_BLOCKED", look.GetProperty("status").GetString());
        Assert.False(look.GetProperty("isActive").GetBoolean());
        Assert.EndsWith("+05:30", look.GetProperty("accessBlockedAt").GetString());
        Assert.True(look.GetProperty("accessBlockedBy").GetInt32() > 0);
    }

    [Fact]
    public async Task A_repeated_block_is_idempotent_and_audited_once()
    {
        await LoginAsync();
        await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = "first" });
        MobileUserProfile first = await ProfileAsync();

        (HttpStatusCode status, JsonElement body) = await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = "second" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Mobile access is already blocked.", body.GetProperty("message").GetString());
        MobileUserProfile second = await ProfileAsync();
        Assert.Equal(first.AccessBlockedAtIst, second.AccessBlockedAtIst);
        Assert.Equal("first", second.AccessBlockReason);
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == AuditAction.MobileAccessBlocked));
    }

    [Fact]
    public async Task The_reason_is_optional_and_limited_to_500_characters()
    {
        Assert.Equal(HttpStatusCode.OK, (await ReadAsync(await _pisAdmin.PutAsync($"{Admin}/{Nsg}/block", null))).Status);   // no body at all
        Assert.Null((await ProfileAsync()).AccessBlockReason);
        await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore");

        (HttpStatusCode tooLong, JsonElement body) = await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = new string('x', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong);
        Assert.Equal("Reason must not exceed 500 characters.", body.GetProperty("message").GetString());
        Assert.True((await ProfileAsync()).IsActive);

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = new string('x', 500) })).Status);
        Assert.Equal(500, (await ProfileAsync()).AccessBlockReason!.Length);
    }

    [Fact]
    public async Task Block_and_restore_are_audited_with_who_what_when_and_why()
    {
        await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = "block reason" });
        await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore", new { reason = "restore reason" });

        await using AppDbContext ctx = _factory.CreateContext();
        MobileUserProfile profile = await ProfileAsync();
        List<AuditLog> logs = await ctx.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.MobileAccessBlocked || a.Action == AuditAction.MobileAccessRestored).OrderBy(a => a.Id).ToListAsync();

        Assert.Equal(new[] { AuditAction.MobileAccessBlocked, AuditAction.MobileAccessRestored }, logs.Select(l => l.Action).ToArray());
        string[] reasons = { "block reason", "restore reason" };
        string[] names = { "Block", "Restore" };
        for (int i = 0; i < 2; i++)
        {
            AuditLog log = logs[i];
            Assert.Equal("Success", log.Result);
            Assert.True(log.UserId > 0);
            Assert.Equal(nameof(MobileUserProfile), log.EntityType);
            Assert.Equal(profile.Id.ToString(), log.EntityId);
            Assert.False(string.IsNullOrEmpty(log.CorrelationId));
            Assert.InRange((NSG.Mobile.Domain.Common.IndianStandardTime.Now - log.CreatedAt).TotalSeconds, -5, 120);   // IST timestamp
            using JsonDocument data = JsonDocument.Parse(log.AdditionalData!);
            Assert.Equal(names[i], data.RootElement.GetProperty("action").GetString());
            Assert.Equal(Nsg, data.RootElement.GetProperty("nsgNumber").GetString());
            Assert.Equal(profile.Id, data.RootElement.GetProperty("mobileProfileId").GetInt64());
            Assert.Equal("AdminPortal", data.RootElement.GetProperty("actorSource").GetString());
            Assert.Equal(log.UserId, data.RootElement.GetProperty("adminProfileId").GetInt32());
            Assert.Equal(reasons[i], data.RootElement.GetProperty("reason").GetString());
            Assert.DoesNotContain(Mpin, log.AdditionalData);
        }
    }

    // ───── restore ─────

    [Fact]
    public async Task Restore_requires_a_fresh_login_and_never_revives_an_old_session()
    {
        Tokens old = await LoginAsync();
        await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block");
        List<MobileRefreshToken> revoked = await TokensAsync();

        (HttpStatusCode status, JsonElement body) = await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore", new { reason = "mistake" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Mobile access restored successfully. The user must log in again.", body.GetProperty("message").GetString());
        Assert.Equal("ACTIVE", body.GetProperty("data").GetProperty("status").GetString());
        Assert.False(body.GetProperty("data").GetProperty("hasActiveSession").GetBoolean());
        MobileUserProfile profile = await ProfileAsync();
        Assert.True(profile.IsActive);
        Assert.Null(profile.AccessBlockedAtIst);
        Assert.Null(profile.AccessBlockedBy);
        Assert.Null(profile.AccessBlockReason);

        // The revoked rows are exactly as they were, and the old tokens do not work.
        List<MobileRefreshToken> now = await TokensAsync();
        Assert.Equal(revoked.Select(t => (t.Id, t.RevokedAtIst, t.RevokeReason)), now.Select(t => (t.Id, t.RevokedAtIst, t.RevokeReason)));
        Assert.False((await ProbeAsync(old.Access)).Alive);
        Assert.NotEqual(HttpStatusCode.OK, (await RefreshAsync(old.Refresh)).Status);
        Assert.Equal(revoked.Count, (await TokensAsync()).Count);

        // A fresh login is the only way back.
        Tokens fresh = await LoginAsync();
        { var probe = await ProbeAsync(fresh.Access); Assert.True(probe.Alive, $"{(int)probe.Status} {probe.Body}"); }
        Assert.False((await ProbeAsync(old.Access)).Alive);

        // Repeating the restore answers with a message.
        (HttpStatusCode againStatus, JsonElement again) = await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore");
        Assert.Equal(HttpStatusCode.OK, againStatus);
        Assert.Equal("Mobile access is already active.", again.GetProperty("message").GetString());
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == AuditAction.MobileAccessRestored));
    }

    [Fact]
    public async Task A_profile_inactive_for_another_reason_can_be_neither_blocked_nor_restored_and_never_gets_the_code()
    {
        Tokens session = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_user_profiles` SET `is_active` = 0");

        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block")).Status);
        MobileUserProfile profile = await ProfileAsync();
        Assert.False(profile.IsActive);
        Assert.Null(profile.AccessBlockedAtIst);                                 // still not an admin block

        JsonElement look = (await GetAsync(_pisAdmin, $"{Admin}/{Nsg}")).Body.GetProperty("data");
        Assert.Equal("INACTIVE", look.GetProperty("status").GetString());

        // Everything the person sees keeps the old answers, without the code.
        (bool alive, HttpStatusCode accessStatus, JsonElement accessBody) = await ProbeAsync(session.Access);
        Assert.False(alive);
        Assert.Equal(HttpStatusCode.Unauthorized, accessStatus);
        AssertNoCode(accessBody, "(access token, inactive profile)");

        (HttpStatusCode refreshStatus, JsonElement refreshBody) = await RefreshAsync(session.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, refreshStatus);
        Assert.Equal("Your access is unavailable. Please contact the IT Admin.", refreshBody.GetProperty("message").GetString());
        AssertNoCode(refreshBody, "(refresh, inactive profile)");

        (HttpStatusCode loginStatus, JsonElement loginBody) = await LoginRawAsync();
        Assert.Equal(HttpStatusCode.Forbidden, loginStatus);
        AssertNoCode(loginBody, "(login, inactive profile)");

        (_, JsonElement check) = await PostAsync(_anonymous, $"{Auth}/login/check", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob });
        Assert.Equal("ACCESS_UNAVAILABLE", check.GetProperty("data").GetProperty("status").GetString());
        AssertNoCode(check, "(login/check, inactive profile)");
        Assert.Equal("Your access is unavailable. Please contact the IT Admin.", check.GetProperty("message").GetString());
    }

    // ───── the code is never returned for anything else ─────

    [Fact]
    public async Task Logout_mpin_change_new_login_expiry_unknown_token_reuse_and_lockout_never_return_the_code()
    {
        // normal logout
        Tokens a = await LoginAsync();
        await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = a.Refresh });
        (bool loggedOutAlive, HttpStatusCode s1, JsonElement b1) = await ProbeAsync(a.Access);
        Assert.False(loggedOutAlive); Assert.Equal(HttpStatusCode.Unauthorized, s1); AssertNoCode(b1, "(access token after logout)");
        (HttpStatusCode r1, JsonElement rb1) = await RefreshAsync(a.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r1); AssertNoCode(rb1, "(refresh after logout)");

        // unknown refresh token
        (HttpStatusCode r2, JsonElement rb2) = await RefreshAsync("not-a-real-token-" + new string('a', 40));
        Assert.Equal(HttpStatusCode.Unauthorized, r2); AssertNoCode(rb2, "(unknown refresh token)");

        // replaced by a new login: that has its own code, SURAKSHA_1002, and never the block code
        Tokens first = await LoginAsync();
        Tokens second = await LoginAsync();
        (bool replacedAlive, HttpStatusCode s3, JsonElement b3) = await ProbeAsync(first.Access);
        Assert.False(replacedAlive); Assert.Equal(HttpStatusCode.Unauthorized, s3);
        Assert.Equal("SURAKSHA_1002", b3.GetProperty("code").GetString());
        (HttpStatusCode r3, JsonElement rb3) = await RefreshAsync(first.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, r3);
        Assert.Equal("SURAKSHA_1002", rb3.GetProperty("code").GetString());

        // MPIN change
        (HttpStatusCode changeStatus, _) = await PostAsync(Bearer(second.Access), $"{Auth}/mpin/change", new { currentMpin = Mpin, newMpin = "1357", confirmNewMpin = "1357" });
        Assert.Equal(HttpStatusCode.OK, changeStatus);
        (bool changedAlive, HttpStatusCode s4, JsonElement b4) = await ProbeAsync(second.Access);
        Assert.False(changedAlive); Assert.Equal(HttpStatusCode.Unauthorized, s4); AssertNoCode(b4, "(access token after MPIN change)");
        (HttpStatusCode r4, JsonElement rb4) = await RefreshAsync(second.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r4); AssertNoCode(rb4, "(refresh after MPIN change)");

        // MPIN reset ends sessions the same way: revoke reason MpinReset
        Tokens afterChange = await LoginAsync("1357");
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_refresh_tokens` SET `revoked_at_ist` = UTC_TIMESTAMP(6), `revoke_reason` = 'MpinReset' WHERE `revoked_at_ist` IS NULL");
        (HttpStatusCode r5, JsonElement rb5) = await RefreshAsync(afterChange.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r5); AssertNoCode(rb5, "(refresh after MPIN reset)");

        // expiry
        Tokens expiring = await LoginAsync("1357");
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_refresh_tokens` SET `expires_at_ist` = '2000-01-01 00:00:00' WHERE `revoked_at_ist` IS NULL");
        (HttpStatusCode r6, JsonElement rb6) = await RefreshAsync(expiring.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r6);
        Assert.Equal("Your session has expired. Please sign in again.", rb6.GetProperty("message").GetString());
        AssertNoCode(rb6, "(expired refresh token)");

        // token reuse
        Tokens reused = await LoginAsync("1357");
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(reused.Refresh)).Status);
        (HttpStatusCode r7, JsonElement rb7) = await RefreshAsync(reused.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r7);
        Assert.Equal("Your session was ended for security reasons. Please sign in again.", rb7.GetProperty("message").GetString());
        AssertNoCode(rb7, "(token reuse)");

        // lockout
        for (int i = 0; i < 5; i++) await LoginRawAsync("0000");
        (HttpStatusCode lockedStatus, JsonElement locked) = await LoginRawAsync("1357");
        Assert.Equal((HttpStatusCode)423, lockedStatus);
        AssertNoCode(locked, "(lockout)");
    }

    [Fact]
    public async Task A_block_is_1001_never_1002_and_a_replaced_session_is_1002_again_only_once_the_block_is_gone()
    {
        Tokens first = await LoginAsync();
        Tokens second = await LoginAsync();                                  // first is replaced (NewLogin), second is live
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block")).Status);

        // the session the block ended: SURAKSHA_1001 on every path
        (bool secondAlive, HttpStatusCode s1, JsonElement b1) = await ProbeAsync(second.Access);
        Assert.False(secondAlive); Assert.Equal(HttpStatusCode.Unauthorized, s1); AssertBlockCode(b1);
        (HttpStatusCode r1, JsonElement rb1) = await RefreshAsync(second.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, r1); AssertBlockCode(rb1);
        (HttpStatusCode l1, JsonElement lb1) = await LoginRawAsync();
        Assert.Equal(HttpStatusCode.Forbidden, l1); AssertBlockCode(lb1);

        // the session replaced before the block: while the block lasts it gets neither code (the pre-existing plain 401)
        (bool firstAlive, HttpStatusCode s2, JsonElement b2) = await ProbeAsync(first.Access);
        Assert.False(firstAlive); Assert.Equal(HttpStatusCode.Unauthorized, s2); AssertNoCode(b2, "(replaced session of a blocked profile, access token)");
        (HttpStatusCode r2, JsonElement rb2) = await RefreshAsync(first.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r2); AssertNoCode(rb2, "(replaced session of a blocked profile, refresh token)");

        // after the restore a fresh login works; the replaced session is "replaced" again, the blocked one keeps its 1001
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore")).Status);
        Tokens third = await LoginAsync();
        Assert.True((await ProbeAsync(third.Access)).Alive);

        (_, HttpStatusCode s3, JsonElement b3) = await ProbeAsync(first.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, s3); Assert.Equal("SURAKSHA_1002", b3.GetProperty("code").GetString());
        (HttpStatusCode r3, JsonElement rb3) = await RefreshAsync(first.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, r3); Assert.Equal("SURAKSHA_1002", rb3.GetProperty("code").GetString());
        (HttpStatusCode r4, JsonElement rb4) = await RefreshAsync(second.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, r4); AssertBlockCode(rb4);
    }

    // ───── races ─────

    [Fact]
    public async Task A_login_racing_a_block_never_leaves_a_usable_token()
    {
        for (int i = 0; i < 12; i++)
        {
            await ResetProfileAsync();

            Task<(HttpStatusCode Status, JsonElement Body)> login = Task.Run(() => LoginRawAsync());
            Task<(HttpStatusCode Status, JsonElement Body)> block = Task.Run(() => PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block", new { reason = "race " + i }));
            await Task.WhenAll(login, block);

            Assert.Equal(HttpStatusCode.OK, block.Result.Status);
            MobileUserProfile profile = await ProfileAsync();
            Assert.True(profile.IsAdminBlocked);
            Assert.False(profile.IsActive);
            Assert.DoesNotContain(await TokensAsync(), t => t.RevokedAtIst == null);          // no live session on a blocked profile

            if (login.Result.Status == HttpStatusCode.OK)
            {
                // The login finished before the block committed; the block must have ended that session.
                Tokens issued = ReadTokens(login.Result.Body);
                Assert.False((await ProbeAsync(issued.Access)).Alive);
                (HttpStatusCode refreshStatus, JsonElement refreshBody) = await RefreshAsync(issued.Refresh);
                Assert.Equal(HttpStatusCode.Forbidden, refreshStatus);
                AssertBlockCode(refreshBody);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, login.Result.Status);
                AssertBlockCode(login.Result.Body);
            }
        }
    }

    [Fact]
    public async Task A_refresh_racing_a_block_never_leaves_a_usable_token()
    {
        for (int i = 0; i < 12; i++)
        {
            await ResetProfileAsync();
            Tokens session = await LoginAsync();

            Task<(HttpStatusCode Status, JsonElement Body)> refresh = Task.Run(() => RefreshAsync(session.Refresh));
            Task<(HttpStatusCode Status, JsonElement Body)> block = Task.Run(() => PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block"));
            await Task.WhenAll(refresh, block);

            Assert.Equal(HttpStatusCode.OK, block.Result.Status);
            Assert.DoesNotContain(await TokensAsync(), t => t.RevokedAtIst == null);

            Assert.True(refresh.Result.Status is HttpStatusCode.OK or HttpStatusCode.Forbidden, $"refresh answered {(int)refresh.Result.Status}: {refresh.Result.Body}");
            if (refresh.Result.Status == HttpStatusCode.OK)
            {
                Tokens rotated = ReadTokens(refresh.Result.Body);
                Assert.False((await ProbeAsync(rotated.Access)).Alive);
                Assert.Equal(HttpStatusCode.Forbidden, (await RefreshAsync(rotated.Refresh)).Status);
            }
            else
            {
                AssertBlockCode(refresh.Result.Body);
            }
            Assert.False((await ProbeAsync(session.Access)).Alive);
        }
    }

    [Fact]
    public async Task Simultaneous_blocks_have_exactly_one_winner_and_one_audit_entry()
    {
        await LoginAsync();

        (HttpStatusCode Status, JsonElement Body)[] results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block"))));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(1, results.Count(r => r.Body.GetProperty("message").GetString()!.StartsWith("Mobile access blocked successfully")));
        Assert.Equal(5, results.Count(r => r.Body.GetProperty("message").GetString() == "Mobile access is already blocked."));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == AuditAction.MobileAccessBlocked));
        Assert.DoesNotContain(await TokensAsync(), t => t.RevokedAtIst == null);
    }

    [Fact]
    public async Task A_restore_racing_a_login_gives_a_consistent_answer_and_a_working_session_when_it_succeeds()
    {
        for (int i = 0; i < 8; i++)
        {
            await ResetProfileAsync();
            await PutAsync(_pisAdmin, $"{Admin}/{Nsg}/block");

            Task<(HttpStatusCode Status, JsonElement Body)> login = Task.Run(() => LoginRawAsync());
            Task<(HttpStatusCode Status, JsonElement Body)> restore = Task.Run(() => PutAsync(_pisAdmin, $"{Admin}/{Nsg}/restore"));
            await Task.WhenAll(login, restore);

            Assert.Equal(HttpStatusCode.OK, restore.Result.Status);
            Assert.True(login.Result.Status is HttpStatusCode.OK or HttpStatusCode.Forbidden, $"login answered {(int)login.Result.Status}");
            if (login.Result.Status == HttpStatusCode.OK)
                Assert.True((await ProbeAsync(ReadTokens(login.Result.Body).Access)).Alive);       // only possible after the restore committed
            else
                AssertBlockCode(login.Result.Body);

            Assert.True((await ProfileAsync()).IsActive);
            Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync()).Status);
        }
    }

    // ───── schema ─────

    [Fact]
    public async Task The_migration_added_the_three_nullable_columns_and_the_check_constraint()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        var columns = await ctx.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(column_name, '|', column_type, '|', is_nullable) AS `Value` FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'mobile_user_profiles' AND column_name LIKE 'access\\_%' ORDER BY column_name").ToListAsync();
        Assert.Equal(new[] { "access_block_reason|varchar(500)|YES", "access_blocked_at_ist|datetime(6)|YES", "access_blocked_by|int|YES" }, columns);

        // The database itself refuses an inconsistent block marker.
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE `mobile_user_profiles` SET `access_blocked_at_ist` = UTC_TIMESTAMP(6) WHERE `nsg_number` = 'ACC-1001'"));        // blocked but still active
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE `mobile_user_profiles` SET `access_block_reason` = 'x' WHERE `nsg_number` = 'ACC-1001'"));                        // reason without a block
    }
}
