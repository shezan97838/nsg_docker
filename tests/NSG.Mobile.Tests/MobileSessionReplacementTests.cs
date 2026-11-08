using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Unit tests (in-memory fakes, no database) of the single active session rule: the latest successful login wins, and a session
/// replaced by it is reported as <c>SURAKSHA_1002</c>. Includes everything that must NOT be reported that way.
/// </summary>
public class MobileSessionReplacementTests
{
    private const string Ip = "127.0.0.1";

    private static MobileRefreshRequestDto Refresh(string token) => new() { RefreshToken = token };

    private static Task<Result<MobileLoginResponseDto>> RefreshAsync(MobileAccessHarness h, string token) => h.Auth.Service.RefreshAsync(Refresh(token), Ip);

    private static Task<MobileSessionState> StateAsync(MobileAccessHarness h, Guid sid) => h.Auth.Sessions.GetSessionStateAsync(sid, h.Profile.Id);

    private sealed record Device(MobileLoginResponseDto Session, Guid Sid);

    private static async Task<Device> SignInAsync(MobileAccessHarness h)
    {
        MobileLoginResponseDto session = await h.LoginAsync();
        return new Device(session, h.Auth.Tokens.LastSessionId);
    }

    private static void AssertReplaced(Result<MobileLoginResponseDto> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.MobileSessionReplaced, result.ErrorCode);
        Assert.Equal("SURAKSHA_1002", result.ErrorCode);
        Assert.Equal("Your session has been terminated because your account was signed in on another device.", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    private static void AssertNotReplaced(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.NotEqual(ApiErrorCodes.MobileSessionReplaced, result.ErrorCode);
        Assert.NotEqual(MobileAuthMessages.SessionReplacedByNewLogin, result.ErrorMessage);
    }

    // ───── latest login wins ─────

    [Fact]
    public async Task Latest_login_wins_across_devices_and_only_the_latest_stays_usable()
    {
        MobileAccessHarness h = new();

        Device a = await SignInAsync(h);
        Assert.Equal(MobileSessionState.Active, await StateAsync(h, a.Sid));

        Device b = await SignInAsync(h);                                  // device B: A is replaced
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));
        Assert.Equal(MobileSessionState.Active, await StateAsync(h, b.Sid));

        Device c = await SignInAsync(h);                                  // third login: B is replaced, A stays replaced
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, b.Sid));
        Assert.Equal(MobileSessionState.Active, await StateAsync(h, c.Sid));

        Device a2 = await SignInAsync(h);                                 // the original device again (same credentials, nothing else): C is replaced
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, c.Sid));
        Assert.Equal(MobileSessionState.Active, await StateAsync(h, a2.Sid));

        Assert.Single(h.Auth.Sessions.Tokens, t => t.RevokedAtIst == null);        // exactly one live refresh token in total
        AssertReplaced(await RefreshAsync(h, a.Session.RefreshToken));
        AssertReplaced(await RefreshAsync(h, b.Session.RefreshToken));
        AssertReplaced(await RefreshAsync(h, c.Session.RefreshToken));
        Assert.True((await RefreshAsync(h, a2.Session.RefreshToken)).IsSuccess);
    }

    [Fact]
    public async Task A_replaced_refresh_token_issues_nothing_and_keeps_the_new_login_reason()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        await SignInAsync(h);
        int issued = h.Auth.Tokens.MobileTokensIssued;
        int rows = h.Auth.Sessions.Tokens.Count;

        AssertReplaced(await RefreshAsync(h, a.Session.RefreshToken));
        AssertReplaced(await RefreshAsync(h, a.Session.RefreshToken));              // and again: nothing escalates

        Assert.Equal(issued, h.Auth.Tokens.MobileTokensIssued);                      // no access token minted
        Assert.Equal(rows, h.Auth.Sessions.Tokens.Count);                            // no refresh token stored
        MobileRefreshToken old = h.Auth.Sessions.Tokens.Single(t => t.FamilyId == a.Sid);
        Assert.Equal(MobileSessionRevokeReasons.NewLogin, old.RevokeReason);
        Assert.Null(old.ReuseDetectedAtIst);
    }

    [Fact]
    public async Task A_session_that_was_refreshed_and_then_replaced_is_still_reported_as_replaced()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        MobileLoginResponseDto rotated = (await RefreshAsync(h, a.Session.RefreshToken)).Data!;
        await SignInAsync(h);

        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));       // the access token of the rotated session
        AssertReplaced(await RefreshAsync(h, rotated.RefreshToken));                  // the newest token of the old session
    }

    // ───── edge A: an old rotated token of a replaced session ─────

    [Fact]
    public async Task An_old_rotated_token_of_a_replaced_session_is_replaced_not_reuse()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        await RefreshAsync(h, a.Session.RefreshToken);                               // a.Session.RefreshToken is now a rotated (used) token
        await SignInAsync(h);                                                         // another device signs in

        Result<MobileLoginResponseDto> result = await RefreshAsync(h, a.Session.RefreshToken);

        AssertReplaced(result);
        Assert.DoesNotContain(h.Auth.Audit.Entries, e => e.Action == AuditAction.TokenReuseDetected);
        MobileRefreshToken[] family = h.Auth.Sessions.Tokens.Where(t => t.FamilyId == a.Sid).ToArray();
        Assert.All(family, t => Assert.Null(t.ReuseDetectedAtIst));
        Assert.Equal(MobileSessionRevokeReasons.NewLogin, family[^1].RevokeReason);   // not relabelled ReuseDetected
    }

    [Fact]
    public async Task Reuse_without_a_replacement_keeps_its_old_behaviour()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        await RefreshAsync(h, a.Session.RefreshToken);

        Result<MobileLoginResponseDto> replay = await RefreshAsync(h, a.Session.RefreshToken);

        AssertNotReplaced(replay);
        Assert.Equal(ApiErrorCodes.RefreshTokenReuseDetected, replay.ErrorCode);
        Assert.Contains(h.Auth.Audit.Entries, e => e.Action == AuditAction.TokenReuseDetected);
        Assert.Equal(MobileSessionRevokeReasons.ReuseDetected, h.Auth.Sessions.Tokens.Last(t => t.FamilyId == a.Sid).RevokeReason);
        Assert.Equal(MobileSessionState.Revoked, await StateAsync(h, a.Sid));
    }

    // ───── edge B: expiry ─────

    [Fact]
    public async Task An_expired_session_that_was_then_replaced_is_replaced_and_one_that_only_expired_is_not()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        h.Auth.Clock.Advance(TimeSpan.FromDays(8));                                   // A's refresh token has expired
        AssertNotReplaced(await RefreshAsync(h, a.Session.RefreshToken));             // expired, nobody replaced it
        Assert.Equal(ApiErrorCodes.RefreshTokenExpired, (await RefreshAsync(h, a.Session.RefreshToken)).ErrorCode);

        await SignInAsync(h);                                                         // a later login revokes the expired-but-unrevoked row
        AssertReplaced(await RefreshAsync(h, a.Session.RefreshToken));                // now it is "replaced"
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));
    }

    // ───── edge C: normal revocation never reports 1002 ─────

    [Fact]
    public async Task Logout_mpin_change_mpin_reset_unknown_and_expiry_never_report_a_replacement()
    {
        MobileAccessHarness logout = new();
        Device a = await SignInAsync(logout);
        await logout.Auth.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = a.Session.RefreshToken }, Ip);
        Assert.Equal(MobileSessionState.Revoked, await StateAsync(logout, a.Sid));
        AssertNotReplaced(await RefreshAsync(logout, a.Session.RefreshToken));

        foreach (string reason in new[] { MobileSessionRevokeReasons.MpinChanged, MobileSessionRevokeReasons.MpinReset })
        {
            MobileAccessHarness m = new();
            Device d = await SignInAsync(m);
            m.Auth.Sessions.RevokeForMpinChange(m.Profile.Id, reason, DateTime.UtcNow);
            Assert.Equal(MobileSessionState.Revoked, await StateAsync(m, d.Sid));
            AssertNotReplaced(await RefreshAsync(m, d.Session.RefreshToken));
        }

        AssertNotReplaced(await RefreshAsync(logout, "no-such-token"));
        Assert.Equal(MobileSessionState.Revoked, await StateAsync(logout, Guid.NewGuid()));
    }

    [Fact]
    public async Task Logging_out_a_replaced_session_does_not_touch_the_active_one_or_relabel_the_replaced_one()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        Device b = await SignInAsync(h);

        await h.Auth.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = a.Session.RefreshToken }, Ip);

        Assert.Equal(MobileSessionState.Active, await StateAsync(h, b.Sid));
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));         // still replaced: logout did not overwrite the reason
    }

    // ───── precedence with the PIS Admin block ─────

    [Fact]
    public async Task A_block_never_reports_a_replacement_and_a_blocked_profile_with_a_replaced_row_gets_neither_code()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        Device b = await SignInAsync(h);                                              // A is replaced (NewLogin row), B is live
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, "r", null);

        // B was live when the block came: SURAKSHA_1001 on every path.
        Assert.Equal(MobileSessionState.AdminBlocked, await StateAsync(h, b.Sid));
        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, (await RefreshAsync(h, b.Session.RefreshToken)).ErrorCode);

        // A already had its NewLogin row: while the block lasts it is neither 1002 nor 1001 (the pre-existing plain answer).
        Assert.Equal(MobileSessionState.Revoked, await StateAsync(h, a.Sid));
        Result<MobileLoginResponseDto> refreshA = await RefreshAsync(h, a.Session.RefreshToken);
        AssertNotReplaced(refreshA);
        Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, refreshA.ErrorCode);
        Assert.Equal(MobileSessionRevokeReasons.NewLogin, h.Auth.Sessions.Tokens.Single(t => t.FamilyId == a.Sid).RevokeReason);
    }

    [Fact]
    public async Task After_a_restore_a_new_login_works_and_sessions_replaced_before_the_block_stay_replaced()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);
        Device b = await SignInAsync(h);
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        await h.Service.RestoreAsync(MobileAuthHarness.Nsg, null, null);

        Device c = await SignInAsync(h);

        Assert.Equal(MobileSessionState.Active, await StateAsync(h, c.Sid));
        Assert.Equal(MobileSessionState.Replaced, await StateAsync(h, a.Sid));         // was replaced before the block
        Assert.Equal(MobileSessionState.AdminBlocked, await StateAsync(h, b.Sid));     // existing behaviour: the block's own session stays 1001
        AssertReplaced(await RefreshAsync(h, a.Session.RefreshToken));
        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, (await RefreshAsync(h, b.Session.RefreshToken)).ErrorCode);
    }

    [Fact]
    public async Task A_login_that_fails_replaces_nothing()
    {
        MobileAccessHarness h = new();
        Device a = await SignInAsync(h);

        Result<MobileLoginResponseDto> wrong = await h.Auth.Service.LoginAsync(new MobileLoginRequestDto
        { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = "0000" }, Ip);

        Assert.False(wrong.IsSuccess);
        Assert.Equal(MobileSessionState.Active, await StateAsync(h, a.Sid));            // only a SUCCESSFUL login replaces
    }
}
