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
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL tests of mobile sessions over HTTP: login, refresh rotation, reuse detection, concurrent refresh, logout, revocation by
/// MPIN change / reset / deactivation, and the boundary between mobile and staff identities. Needs NSG_TEST_MYSQL.
/// </summary>
[Collection("mysql")]
public class MobileSessionDatabaseTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/mobile-auth";
    private const string Approvals = "/api/v1/mobile-approval-requests";
    private const string Nsg = "SESS-1001", Mobile = "9823456701", Dob = "1992-07-08", Mpin = "2580";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public MobileSessionDatabaseTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.MobileUserProfiles.Add(new MobileUserProfile
        {
            NsgNumber = Nsg, MobileNumber = Mobile, DateOfBirth = DateOnly.Parse(Dob), Role = UserRole.Employee,
            MpinHash = new PasswordHashingService().HashPassword(Mpin)
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_auth_grants`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, object body)
    {
        HttpResponseMessage response = await client.PostAsync(url, Json(body));
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"{url} returned a blank body ({(int)response.StatusCode}).");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private record Tokens(string Access, string Refresh);

    private async Task<Tokens> LoginAsync(string mpin = Mpin)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin });
        Assert.Equal(HttpStatusCode.OK, status);
        return ReadTokens(body);
    }

    private static Tokens ReadTokens(JsonElement body)
    {
        JsonElement data = body.GetProperty("data");
        return new Tokens(data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    private HttpClient Bearer(string accessToken)
    {
        HttpClient client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(string refreshToken) => PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken });

    /// <summary>mpin/change with a wrong current MPIN: 401 for a bad access token, and also 401 (invalid credentials) for a good one, so the body tells them apart.</summary>
    private async Task<bool> AccessTokenWorksAsync(string accessToken)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(Bearer(accessToken), $"{Auth}/mpin/change", new { currentMpin = "9999", newMpin = "1357", confirmNewMpin = "1357" });
        // A live session reaches the service, which answers with the "Invalid credentials." ProblemDetails; a dead one is rejected before it.
        return status == HttpStatusCode.Unauthorized && body.TryGetProperty("title", out JsonElement title) && title.GetString() == "AUTH_INVALID_CREDENTIALS";
    }

    // ───── login and storage ─────

    [Fact]
    public async Task Login_returns_both_tokens_and_the_database_holds_only_a_hash_of_the_refresh_token()
    {
        Tokens tokens = await LoginAsync();
        Assert.False(string.IsNullOrEmpty(tokens.Refresh));
        Assert.True(await AccessTokenWorksAsync(tokens.Access));

        await using AppDbContext ctx = _factory.CreateContext();
        MobileRefreshToken stored = await ctx.MobileRefreshTokens.SingleAsync();
        Assert.NotEqual(tokens.Refresh, stored.TokenHash);
        Assert.DoesNotContain(tokens.Refresh, stored.TokenHash);
        Assert.Equal((await ctx.MobileUserProfiles.SingleAsync()).Id, stored.ProfileId);
        Assert.True(stored.ExpiresAtIst > IndianStandardTime.Now.AddDays(6));
        Assert.Null(stored.RevokedAtIst);
    }

    [Fact]
    public async Task A_wrong_mpin_stores_no_session()
    {
        (HttpStatusCode status, _) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(0, await ctx.MobileRefreshTokens.CountAsync());
    }

    // ───── rotation and reuse ─────

    [Fact]
    public async Task Refresh_rotates_and_a_replayed_token_ends_the_session_for_everyone_holding_it()
    {
        Tokens first = await LoginAsync();
        (HttpStatusCode okStatus, JsonElement ok) = await RefreshAsync(first.Refresh);
        Assert.Equal(HttpStatusCode.OK, okStatus);
        Tokens second = ReadTokens(ok);
        Assert.NotEqual(first.Refresh, second.Refresh);
        Assert.True(await AccessTokenWorksAsync(second.Access));

        (HttpStatusCode replayStatus, JsonElement replay) = await RefreshAsync(first.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, replayStatus);
        Assert.Equal("TOKEN_REUSE_DETECTED", replay.GetProperty("title").GetString());

        // The newest token and its access token died with the session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.Refresh)).Status);
        Assert.False(await AccessTokenWorksAsync(second.Access));

        await using AppDbContext ctx = _factory.CreateContext();
        List<MobileRefreshToken> rows = await ctx.MobileRefreshTokens.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.Select(r => r.FamilyId).Distinct());
        Assert.All(rows, r => Assert.NotNull(r.RevokedAtIst));
        Assert.NotNull(rows[0].ReuseDetectedAtIst);
        Assert.True(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditAction.TokenReuseDetected));
    }

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_have_exactly_one_winner()
    {
        Tokens tokens = await LoginAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RefreshAsync(tokens.Refresh)));

        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.Status == HttpStatusCode.Unauthorized));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(2, await ctx.MobileRefreshTokens.CountAsync());            // the original and exactly one successor
        Assert.Equal(0, await ctx.MobileRefreshTokens.CountAsync(t => t.RevokedAtIst == null));   // losers triggered reuse handling: nothing is left live
    }

    [Fact]
    public async Task Unknown_and_expired_refresh_tokens_are_401_with_a_body()
    {
        (HttpStatusCode unknown, JsonElement unknownBody) = await RefreshAsync("not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown);
        Assert.Equal("TOKEN_INVALID", unknownBody.GetProperty("title").GetString());

        Tokens tokens = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_refresh_tokens` SET `expires_at_ist` = `expires_at_ist` - INTERVAL 30 DAY");
        (HttpStatusCode expired, JsonElement expiredBody) = await RefreshAsync(tokens.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, expired);
        Assert.Equal("TOKEN_EXPIRED", expiredBody.GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = "" })).Status);
    }

    // ───── logout and revocation ─────

    [Fact]
    public async Task Logout_ends_the_session_immediately_and_is_idempotent()
    {
        Tokens tokens = await LoginAsync();
        (HttpStatusCode status, JsonElement body) = await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = tokens.Refresh });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("You have been logged out.", body.GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.Refresh)).Status);
        Assert.False(await AccessTokenWorksAsync(tokens.Access));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = tokens.Refresh })).Status);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = "unknown" })).Status);
    }

    [Fact]
    public async Task A_new_login_ends_the_previous_session()
    {
        Tokens old = await LoginAsync();
        Tokens current = await LoginAsync();
        Assert.False(await AccessTokenWorksAsync(old.Access));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(old.Refresh)).Status);
        Assert.True(await AccessTokenWorksAsync(current.Access));
    }

    [Fact]
    public async Task Changing_the_mpin_revokes_the_session_and_the_new_mpin_logs_in()
    {
        Tokens tokens = await LoginAsync();
        (HttpStatusCode status, _) = await PostAsync(Bearer(tokens.Access), $"{Auth}/mpin/change", new { currentMpin = Mpin, newMpin = "1357", confirmNewMpin = "1357" });
        Assert.Equal(HttpStatusCode.OK, status);

        Assert.False(await AccessTokenWorksAsync(tokens.Access));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.Refresh)).Status);
        await using (AppDbContext ctx = _factory.CreateContext())
            Assert.Equal(MobileSessionRevokeReasons.MpinChanged, (await ctx.MobileRefreshTokens.SingleAsync()).RevokeReason);

        Tokens again = await LoginAsync("1357");
        Assert.False(string.IsNullOrEmpty(again.Refresh));
    }

    [Fact]
    public async Task Resetting_the_mpin_revokes_the_session()
    {
        Tokens tokens = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.Add(new UserPis { NsgNumber = Nsg, ServiceNumber = "S", MobileNumber = Mobile, DateOfBirth = DateOnly.Parse(Dob), MobileAppActivationStatus = MobileAppActivationStatus.Approved });
            await ctx.SaveChangesAsync();
        }

        (_, JsonElement sent) = await PostAsync(_anonymous, $"{Auth}/otp/send", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow = "MpinReset" });
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        (_, JsonElement verified) = await PostAsync(_anonymous, $"{Auth}/otp/verify", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, flow = "MpinReset", otpReferenceId = reference, otpCode = "123456" });
        string grant = verified.GetProperty("data").GetProperty("grantToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/mpin/reset", new { grantToken = grant, newMpin = "2468", confirmNewMpin = "2468" })).Status);

        Assert.False(await AccessTokenWorksAsync(tokens.Access));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.Refresh)).Status);
        await using AppDbContext check = _factory.CreateContext();
        Assert.Equal(0, await check.MobileRefreshTokens.CountAsync(t => t.RevokedAtIst == null));
    }

    [Fact]
    public async Task Deactivating_the_profile_kills_the_access_token_the_refresh_token_and_login()
    {
        Tokens tokens = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_user_profiles` SET `is_active` = 0");

        Assert.False(await AccessTokenWorksAsync(tokens.Access));    // rejected by the session check, not only at expiry

        (HttpStatusCode refresh, JsonElement refreshBody) = await RefreshAsync(tokens.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, refresh);
        Assert.Equal("Your access is unavailable. Please contact the IT Admin.", refreshBody.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin = Mpin })).Status);
        await using AppDbContext check = _factory.CreateContext();
        Assert.Equal(0, await check.MobileRefreshTokens.CountAsync(t => t.RevokedAtIst == null));
    }

    // ───── identity boundaries ─────

    [Fact]
    public async Task Mobile_and_staff_tokens_do_not_cross_over()
    {
        Tokens tokens = await LoginAsync();
        HttpClient mobile = Bearer(tokens.Access);

        // A mobile token is not an administrator.
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync(Approvals)).StatusCode);   // admin-portal endpoint: a mobile token is not an admin token

        // A staff token (even PisAdmin) is not a mobile session: it has no profile claim and fails the mobile policy.
        HttpClient admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Auth}/mpin/change", Json(new { currentMpin = Mpin, newMpin = "1357", confirmNewMpin = "1357" }))).StatusCode);

        // A refresh token is not a bearer token, and an access token is not a refresh token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(tokens.Refresh).PostAsync($"{Auth}/mpin/change", Json(new { currentMpin = Mpin, newMpin = "1357", confirmNewMpin = "1357" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(tokens.Access)).Status);   // far longer than any refresh token: rejected as invalid input
    }

    [Fact]
    public async Task The_database_blocks_deleting_a_profile_that_still_has_sessions()
    {
        await LoginAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`"));
    }
}
