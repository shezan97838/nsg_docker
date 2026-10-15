using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Unit tests (in-memory fakes, no database) of mobile sessions: login, refresh rotation, reuse detection, logout, revocation.</summary>
public class MobileSessionTests
{
    private const string Ip = "127.0.0.1";

    private static MobileLoginRequestDto Login(string mpin = MobileAuthHarness.Mpin) => new()
    { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = mpin };

    private static async Task<(MobileAuthHarness H, MobileUserProfile P, MobileLoginResponseDto Session)> SignedInAsync()
    {
        MobileAuthHarness h = new();
        MobileUserProfile p = h.AddProfile();
        MobileLoginResponseDto session = (await h.Service.LoginAsync(Login(), Ip)).Data!;
        return (h, p, session);
    }

    [Fact]
    public async Task Login_issues_an_access_token_and_a_hashed_stored_refresh_token()
    {
        var (h, p, session) = await SignedInAsync();

        Assert.False(string.IsNullOrEmpty(session.AccessToken));
        Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        MobileRefreshToken stored = Assert.Single(h.Sessions.Tokens);
        Assert.NotEqual(session.RefreshToken, stored.TokenHash);                       // hash only
        Assert.Equal(h.Tokens.HashToken(session.RefreshToken), stored.TokenHash);
        Assert.Equal(p.Id, stored.ProfileId);                                           // a mobile profile, never a staff user
        Assert.Equal(h.Tokens.LastSessionId, stored.FamilyId);
        Assert.Equal(IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddDays(7), stored.ExpiresAtIst);
        Assert.Equal(TimeSpan.FromHours(5.5), session.RefreshTokenExpiresAt.Offset);
    }

    [Fact]
    public async Task Failed_login_issues_no_refresh_token()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        Assert.False((await h.Service.LoginAsync(Login("0000"), Ip)).IsSuccess);
        Assert.Empty(h.Sessions.Tokens);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_within_the_same_session()
    {
        var (h, _, session) = await SignedInAsync();
        var refreshed = await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip);

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(session.RefreshToken, refreshed.Data!.RefreshToken);
        Assert.Equal(2, h.Sessions.Tokens.Count);
        Assert.Single(h.Sessions.Tokens.Select(t => t.FamilyId).Distinct());
        Assert.Equal(MobileSessionRevokeReasons.Rotated, h.Sessions.Tokens[0].RevokeReason);
        Assert.Null(h.Sessions.Tokens[1].RevokedAtIst);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.TokenRefreshed);
    }

    [Fact]
    public async Task A_rotated_token_cannot_be_used_again_and_reuse_ends_the_whole_session()
    {
        var (h, _, session) = await SignedInAsync();
        var second = (await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip)).Data!;

        var replay = await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip);
        Assert.Equal(ApiErrorCodes.RefreshTokenReuseDetected, replay.ErrorCode);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.TokenReuseDetected);

        // The legitimate holder of the newest token is signed out too.
        var newest = await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = second.RefreshToken }, Ip);
        Assert.False(newest.IsSuccess);
        Assert.All(h.Sessions.Tokens, t => Assert.NotNull(t.RevokedAtIst));
        Assert.False(await h.Sessions.IsSessionActiveAsync(h.Tokens.LastSessionId, 1));
    }

    [Fact]
    public async Task Unknown_and_expired_refresh_tokens_are_refused()
    {
        var (h, _, session) = await SignedInAsync();
        Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, (await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = "nonsense" }, Ip)).ErrorCode);

        h.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(ApiErrorCodes.RefreshTokenExpired, (await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip)).ErrorCode);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_is_idempotent_and_silent_about_unknown_tokens()
    {
        var (h, _, session) = await SignedInAsync();
        var first = await h.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = session.RefreshToken }, Ip);
        var again = await h.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = session.RefreshToken }, Ip);
        var unknown = await h.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = "who-knows" }, Ip);

        foreach (var r in new[] { first, again, unknown })
        {
            Assert.True(r.IsSuccess);
            Assert.Equal(MobileAuthStatus.LoggedOut, r.Data!.Status);
            Assert.Equal("You have been logged out.", r.Data.Message);
        }
        Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, (await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip)).ErrorCode);   // logged-out, not "reuse"
        Assert.False(await h.Sessions.IsSessionActiveAsync(h.Tokens.LastSessionId, 1));
    }

    [Fact]
    public async Task A_new_login_ends_the_previous_session()
    {
        var (h, _, first) = await SignedInAsync();
        Guid firstSession = h.Tokens.LastSessionId;
        var second = (await h.Service.LoginAsync(Login(), Ip)).Data!;

        Assert.NotEqual(firstSession, h.Tokens.LastSessionId);
        Assert.False((await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = first.RefreshToken }, Ip)).IsSuccess);
        Assert.True((await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = second.RefreshToken }, Ip)).IsSuccess);
    }

    [Fact]
    public async Task Changing_the_mpin_revokes_every_session()
    {
        var (h, p, session) = await SignedInAsync();
        Guid sid = h.Tokens.LastSessionId;
        h.CurrentUser.ProfileId = p.Id;
        Assert.True((await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = MobileAuthHarness.Mpin, NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip)).IsSuccess);

        Assert.False(await h.Sessions.IsSessionActiveAsync(sid, p.Id));                   // the access token's session check now fails
        Assert.False((await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip)).IsSuccess);
        Assert.True((await h.Service.LoginAsync(Login("1357"), Ip)).IsSuccess);
    }

    [Fact]
    public async Task Resetting_the_mpin_revokes_every_session()
    {
        var (h, p, session) = await SignedInAsync();
        Guid sid = h.Tokens.LastSessionId;
        var verified = await h.SendAndVerifyAsync(MobileOtpFlow.MpinReset);
        Assert.True((await h.Service.ResetMpinAsync(new MpinResetRequestDto { GrantToken = verified.Data!.GrantToken!, NewMpin = "2468", ConfirmNewMpin = "2468" }, Ip)).IsSuccess);

        Assert.False(await h.Sessions.IsSessionActiveAsync(sid, p.Id));
        Assert.False((await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip)).IsSuccess);
    }

    [Fact]
    public async Task A_deactivated_profile_cannot_refresh_log_in_or_keep_an_access_token_alive()
    {
        var (h, p, session) = await SignedInAsync();
        Guid sid = h.Tokens.LastSessionId;
        p.IsActive = false;

        Assert.False(await h.Sessions.IsSessionActiveAsync(sid, p.Id));                   // access token rejected at once
        var refresh = await h.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, Ip);
        Assert.Equal(ApiErrorCodes.Forbidden, refresh.ErrorCode);
        Assert.Equal(MobileAuthMessages.AccessUnavailable, refresh.ErrorMessage);
        Assert.All(h.Sessions.Tokens, t => Assert.NotNull(t.RevokedAtIst));

        Assert.Equal(ApiErrorCodes.Forbidden, (await h.Service.LoginAsync(Login(), Ip)).ErrorCode);
    }

    [Fact]
    public async Task Refresh_tokens_of_different_profiles_never_mix()
    {
        MobileAuthHarness h = new();
        MobileUserProfile a = h.AddProfile();
        MobileUserProfile b = h.AddProfile(nsg: "NSG-2002", mobile: "9812345678");
        await h.Service.LoginAsync(Login(), Ip);
        await h.Service.LoginAsync(new MobileLoginRequestDto { NsgNumber = "NSG-2002", MobileNumber = "9812345678", DateOfBirth = MobileAuthHarness.Dob, Mpin = MobileAuthHarness.Mpin }, Ip);

        Assert.Equal(new[] { a.Id, b.Id }, h.Sessions.Tokens.Select(t => t.ProfileId).OrderBy(x => x));
        Assert.All(h.Sessions.Tokens, t => Assert.Null(t.RevokedAtIst));   // logging in as B did not end A's session
    }

    [Fact]
    public void Refresh_and_logout_requests_are_validated()
    {
        Assert.False(new MobileRefreshRequestValidator().Validate(new MobileRefreshRequestDto()).IsValid);
        Assert.False(new MobileLogoutRequestValidator().Validate(new MobileLogoutRequestDto { RefreshToken = new string('x', 201) }).IsValid);
        Assert.True(new MobileRefreshRequestValidator().Validate(new MobileRefreshRequestDto { RefreshToken = "abc" }).IsValid);
    }
}
