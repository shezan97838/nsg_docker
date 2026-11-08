using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL tests, over HTTP and the real authentication pipeline, of "latest successful login wins" and <c>SURAKSHA_1002</c>:
/// sequential replacement, the code on the access-token 401 and the refresh-token 403, the edge cases (rotated token, expiry), and the
/// races between logins and between a login and a refresh. Needs NSG_TEST_MYSQL (a database whose name ends in "_test").
/// </summary>
[Collection("mysql")]
public class MobileSessionReplacementDatabaseTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/mobile-auth";
    private const string Nsg = "REPL-1001", Mobile = "9823456703", Dob = "1992-07-08", Mpin = "2580";
    private const string ReplacedCode = "SURAKSHA_1002";
    private const string ReplacedMessage = "Your session has been terminated because your account was signed in on another device.";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public MobileSessionReplacementDatabaseTests(DatabaseFixture fixture)
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
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    // ───── helpers ─────

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"A blank body ({(int)response.StatusCode}).");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, object body)
        => await ReadAsync(await client.PostAsync(url, Json(body)));

    private record Tokens(string Access, string Refresh);

    private Task<(HttpStatusCode Status, JsonElement Body)> LoginRawAsync(string mpin = Mpin)
        => PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = Nsg, mobileNumber = Mobile, dateOfBirth = Dob, mpin });

    private async Task<Tokens> LoginAsync()
    {
        (HttpStatusCode status, JsonElement body) = await LoginRawAsync();
        Assert.Equal(HttpStatusCode.OK, status);
        return ReadTokens(body);
    }

    private static Tokens ReadTokens(JsonElement body)
        => new(body.GetProperty("data").GetProperty("accessToken").GetString()!, body.GetProperty("data").GetProperty("refreshToken").GetString()!);

    private Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(string refreshToken) => PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken });

    private HttpClient Bearer(string accessToken)
    {
        HttpClient client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// An authenticated call: mpin/change with a wrong current MPIN. A live session reaches the service (401 "Current MPIN is incorrect.", which
    /// counts a failed attempt, so the counter is reset straight after); a dead one is refused before it, with the body returned.
    /// </summary>
    private async Task<(bool Alive, HttpStatusCode Status, JsonElement Body)> ProbeAsync(string accessToken)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(Bearer(accessToken), $"{Auth}/mpin/change", new { currentMpin = "9999", newMpin = "1357", confirmNewMpin = "1357" });
        bool alive = status == HttpStatusCode.Unauthorized && body.TryGetProperty("message", out JsonElement m) && m.GetString() == "Current MPIN is incorrect.";
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_user_profiles` SET `failed_login_attempts` = 0, `locked_until_ist` = NULL");
        return (alive, status, body);
    }

    private static void AssertReplaced(JsonElement body)
    {
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(ReplacedCode, body.GetProperty("code").GetString());
        Assert.Equal(ReplacedMessage, body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    private static void AssertNoCode(JsonElement body, string because)
        => Assert.False(body.TryGetProperty("code", out JsonElement code), $"No public code may be returned {because}: {code}");

    private async Task AssertReplacedEverywhereAsync(Tokens old)
    {
        (bool alive, HttpStatusCode accessStatus, JsonElement accessBody) = await ProbeAsync(old.Access);
        Assert.False(alive);
        Assert.Equal(HttpStatusCode.Unauthorized, accessStatus);
        AssertReplaced(accessBody);

        (HttpStatusCode refreshStatus, JsonElement refreshBody) = await RefreshAsync(old.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, refreshStatus);
        AssertReplaced(refreshBody);
    }

    private async Task<List<MobileRefreshToken>> RowsAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.MobileRefreshTokens.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
    }

    // ───── sequential replacement ─────

    [Fact]
    public async Task Each_new_login_replaces_the_previous_session_and_only_the_latest_stays_usable()
    {
        Tokens a = await LoginAsync();
        Assert.True((await ProbeAsync(a.Access)).Alive);

        Tokens b = await LoginAsync();                       // device B
        await AssertReplacedEverywhereAsync(a);
        Assert.True((await ProbeAsync(b.Access)).Alive);

        Tokens c = await LoginAsync();                       // device C
        await AssertReplacedEverywhereAsync(a);
        await AssertReplacedEverywhereAsync(b);
        Assert.True((await ProbeAsync(c.Access)).Alive);

        Tokens a2 = await LoginAsync();                      // the original device again
        await AssertReplacedEverywhereAsync(c);
        Assert.True((await ProbeAsync(a2.Access)).Alive);

        List<MobileRefreshToken> rows = await RowsAsync();
        Assert.Single(rows, r => r.RevokedAtIst == null);
        Assert.Equal(3, rows.Count(r => r.RevokeReason == MobileSessionRevokeReasons.NewLogin));   // a, b, c; the revoke reason column is reused as it is
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(a2.Refresh)).Status);                  // the latest can still rotate
    }

    [Fact]
    public async Task A_replaced_refresh_issues_nothing_and_leaves_the_rows_untouched()
    {
        Tokens a = await LoginAsync();
        await LoginAsync();
        List<MobileRefreshToken> before = await RowsAsync();

        await AssertReplacedEverywhereAsync(a);
        await AssertReplacedEverywhereAsync(a);

        List<MobileRefreshToken> after = await RowsAsync();
        Assert.Equal(before.Count, after.Count);                                                    // nothing stored
        Assert.Equal(before.Select(r => (r.Id, r.RevokedAtIst, r.RevokeReason, r.ReuseDetectedAtIst)), after.Select(r => (r.Id, r.RevokedAtIst, r.RevokeReason, r.ReuseDetectedAtIst)));
    }

    // ───── edge cases ─────

    [Fact]
    public async Task An_old_rotated_token_of_a_replaced_session_is_replaced_not_reuse_but_plain_reuse_is_unchanged()
    {
        // not replaced: the genuine reuse behaviour
        Tokens x = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(x.Refresh)).Status);
        (HttpStatusCode reuseStatus, JsonElement reuseBody) = await RefreshAsync(x.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseStatus);
        Assert.Equal("Your session was ended for security reasons. Please sign in again.", reuseBody.GetProperty("message").GetString());
        AssertNoCode(reuseBody, "(token reuse without a replacement)");

        // replaced: the rotated token is presented after another device signed in
        Tokens a = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(a.Refresh)).Status);                   // a.Refresh is now a used token
        await LoginAsync();                                                                          // another device

        (HttpStatusCode status, JsonElement body) = await RefreshAsync(a.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        AssertReplaced(body);

        await using AppDbContext ctx = _factory.CreateContext();
        string hash = new JwtTokenSha().Hash(a.Refresh);
        MobileRefreshToken presented = await ctx.MobileRefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash);
        Assert.Null(presented.ReuseDetectedAtIst);                                                  // not stamped as theft
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(l => l.Action == AuditAction.TokenReuseDetected));   // only the genuine reuse above
        List<MobileRefreshToken> family = await ctx.MobileRefreshTokens.AsNoTracking().Where(t => t.FamilyId == presented.FamilyId).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(MobileSessionRevokeReasons.NewLogin, family[^1].RevokeReason);                 // the original reason is preserved
    }

    [Fact]
    public async Task Expired_then_replaced_is_1002_and_expired_alone_is_not()
    {
        Tokens a = await LoginAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `mobile_refresh_tokens` SET `expires_at_ist` = '2000-01-01 00:00:00' WHERE `revoked_at_ist` IS NULL");

        (HttpStatusCode expiredStatus, JsonElement expiredBody) = await RefreshAsync(a.Refresh);   // expired, nobody replaced it
        Assert.Equal(HttpStatusCode.Unauthorized, expiredStatus);
        Assert.Equal("Your session has expired. Please sign in again.", expiredBody.GetProperty("message").GetString());
        AssertNoCode(expiredBody, "(expired, not replaced)");

        await LoginAsync();                                                                          // now a newer login replaces the expired row
        (HttpStatusCode status, JsonElement body) = await RefreshAsync(a.Refresh);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        AssertReplaced(body);
    }

    [Fact]
    public async Task Logout_and_mpin_change_never_report_a_replacement_for_the_session_they_ended()
    {
        Tokens a = await LoginAsync();
        await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = a.Refresh });
        (bool aliveAfterLogout, HttpStatusCode s1, JsonElement b1) = await ProbeAsync(a.Access);
        Assert.False(aliveAfterLogout); Assert.Equal(HttpStatusCode.Unauthorized, s1); AssertNoCode(b1, "(access token after logout)");
        (HttpStatusCode r1, JsonElement rb1) = await RefreshAsync(a.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r1); AssertNoCode(rb1, "(refresh after logout)");

        Tokens b = await LoginAsync();
        (HttpStatusCode changeStatus, _) = await PostAsync(Bearer(b.Access), $"{Auth}/mpin/change", new { currentMpin = Mpin, newMpin = "1357", confirmNewMpin = "1357" });
        Assert.Equal(HttpStatusCode.OK, changeStatus);
        (bool aliveAfterChange, HttpStatusCode s2, JsonElement b2) = await ProbeAsync(b.Access);
        Assert.False(aliveAfterChange); Assert.Equal(HttpStatusCode.Unauthorized, s2); AssertNoCode(b2, "(access token after MPIN change)");
        (HttpStatusCode r2, JsonElement rb2) = await RefreshAsync(b.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, r2); AssertNoCode(rb2, "(refresh after MPIN change)");
    }

    // ───── concurrency ─────

    [Fact]
    public async Task Simultaneous_logins_leave_exactly_one_usable_session()
    {
        for (int round = 0; round < 5; round++)
        {
            await using (AppDbContext ctx = _factory.CreateContext())
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");

            (HttpStatusCode Status, JsonElement Body)[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => LoginRawAsync())));
            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));                    // a deadlock or retry exhaustion would show here as a 500
            Tokens[] issued = results.Select(r => ReadTokens(r.Body)).ToArray();

            List<MobileRefreshToken> rows = await RowsAsync();
            Assert.Equal(6, rows.Count);
            Assert.Single(rows, r => r.RevokedAtIst == null);                                       // one live refresh token
            Assert.Equal(5, rows.Count(r => r.RevokeReason == MobileSessionRevokeReasons.NewLogin));

            int alive = 0;
            foreach (Tokens t in issued)
            {
                if ((await ProbeAsync(t.Access)).Alive) alive++;
            }
            Assert.Equal(1, alive);                                                                 // exactly one usable access token

            int refreshed = 0, replaced = 0;
            foreach (Tokens t in issued)
            {
                (HttpStatusCode status, JsonElement body) = await RefreshAsync(t.Refresh);
                if (status == HttpStatusCode.OK) refreshed++;
                else { Assert.Equal(HttpStatusCode.Forbidden, status); AssertReplaced(body); replaced++; }
            }
            Assert.Equal(1, refreshed);
            Assert.Equal(5, replaced);
        }
    }

    [Fact]
    public async Task A_login_racing_a_refresh_never_leaves_the_old_session_usable()
    {
        for (int round = 0; round < 10; round++)
        {
            await using (AppDbContext ctx = _factory.CreateContext())
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
            Tokens a = await LoginAsync();

            Task<(HttpStatusCode Status, JsonElement Body)> refresh = Task.Run(() => RefreshAsync(a.Refresh));
            Task<(HttpStatusCode Status, JsonElement Body)> login = Task.Run(() => LoginRawAsync());
            await Task.WhenAll(refresh, login);

            Assert.Equal(HttpStatusCode.OK, login.Result.Status);
            Tokens b = ReadTokens(login.Result.Body);
            Assert.True(refresh.Result.Status is HttpStatusCode.OK or HttpStatusCode.Forbidden, $"refresh answered {(int)refresh.Result.Status}: {refresh.Result.Body}");

            // After both committed, only B's login is usable: one live row, B's access token alive, A's old and (if issued) rotated tokens dead.
            Assert.Single(await RowsAsync(), r => r.RevokedAtIst == null);
            Assert.True((await ProbeAsync(b.Access)).Alive);
            await AssertReplacedEverywhereAsync(a);
            if (refresh.Result.Status == HttpStatusCode.OK)
            {
                Tokens rotated = ReadTokens(refresh.Result.Body);                                   // handed out just before B's login committed
                Assert.False((await ProbeAsync(rotated.Access)).Alive);
                (HttpStatusCode status, JsonElement body) = await RefreshAsync(rotated.Refresh);
                Assert.Equal(HttpStatusCode.Forbidden, status);
                AssertReplaced(body);
            }
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(b.Refresh)).Status);
        }
    }

    [Fact]
    public async Task Repeated_login_and_rotation_keep_exactly_one_live_session()
    {
        Tokens previous = await LoginAsync();
        for (int i = 0; i < 8; i++)
        {
            (HttpStatusCode status, JsonElement body) = await RefreshAsync(previous.Refresh);
            Assert.Equal(HttpStatusCode.OK, status);
            Tokens rotated = ReadTokens(body);
            Assert.Single(await RowsAsync(), r => r.RevokedAtIst == null);

            Tokens next = await LoginAsync();                                                       // the "other device"
            await AssertReplacedEverywhereAsync(rotated);
            Assert.True((await ProbeAsync(next.Access)).Alive);
            Assert.Single(await RowsAsync(), r => r.RevokedAtIst == null);
            previous = next;
        }
    }

    /// <summary>The SHA-256/base64 hash the application stores, computed the same way, so a test can find the row of a token it holds.</summary>
    private sealed class JwtTokenSha
    {
        public string Hash(string token) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
