using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.MobileAccess;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Unit tests (no database) of what a PIS Admin block means for the mobile endpoints: login, login/check, refresh and the access-token
/// session check return <c>SURAKSHA_1001</c>; every other way a session or a login can end never does.
/// </summary>
public class MobileAccessBlockFlowTests
{
    private const string Ip = "127.0.0.1";
    private const string Code = "SURAKSHA_1001";

    private static MobileLoginRequestDto Login(string mpin = MobileAuthHarness.Mpin) => new()
    { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = mpin };

    private static MobileRefreshRequestDto Refresh(string token) => new() { RefreshToken = token };

    private static void AssertNotTheBlockCode(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.NotEqual(ApiErrorCodes.MobileAccessBlocked, result.ErrorCode);
        Assert.NotEqual(MobileAuthMessages.AccessBlockedByPisAdmin, result.ErrorMessage);
    }

    [Fact]
    public void The_code_has_the_exact_agreed_value_and_is_one_of_exactly_two_public_codes()
    {
        Assert.Equal("SURAKSHA_1001", ApiErrorCodes.MobileAccessBlocked);
        Assert.Equal("SURAKSHA_1002", ApiErrorCodes.MobileSessionReplaced);
        Assert.Equal(new[] { "SURAKSHA_1001", "SURAKSHA_1002" }, ApiErrorCodes.PublicCodes.OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.Equal("Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.", MobileAuthMessages.AccessBlockedByPisAdmin);
    }

    // ───── login ─────

    [Fact]
    public async Task A_blocked_profile_cannot_log_in_even_with_the_right_mpin_and_gets_no_token_and_no_session()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, "r", null);
        int tokensBefore = h.Auth.Tokens.MobileTokensIssued;
        int rowsBefore = h.Auth.Sessions.Tokens.Count;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.LoginAsync(Login(), Ip);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, result.ErrorCode);
        Assert.Equal(MobileAuthMessages.AccessBlockedByPisAdmin, result.ErrorMessage);
        Assert.Null(result.Data);
        Assert.Equal(tokensBefore, h.Auth.Tokens.MobileTokensIssued);            // no access token minted
        Assert.Equal(rowsBefore, h.Auth.Sessions.Tokens.Count);                   // no refresh token, no session
        Assert.Equal(0, h.Profile.FailedLoginAttempts);                           // a valid MPIN is not a failed attempt
        Assert.True(h.Profile.IsAdminBlocked);                                    // login does not touch the block
        Assert.False(h.Profile.IsActive);
        Assert.Contains(h.Auth.Audit.Entries, e => e.Action == AuditAction.LoginFailure && e.Data!.Contains("AdminBlocked"));
    }

    [Fact]
    public async Task A_wrong_mpin_on_a_blocked_profile_is_not_counted_either()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);

        for (int i = 0; i < 5; i++)
            Assert.Equal(ApiErrorCodes.MobileAccessBlocked, (await h.Auth.Service.LoginAsync(Login("0000"), Ip)).ErrorCode);

        Assert.Equal(0, h.Profile.FailedLoginAttempts);
        Assert.Null(h.Profile.LockedUntilIst);                                    // and it never turns into a lockout
    }

    [Fact]
    public async Task A_block_that_commits_between_the_login_check_and_the_session_insert_still_issues_nothing()
    {
        MobileAccessHarness h = new();
        h.Auth.Sessions.BeforeStartSession = () => h.Service.BlockAsync(MobileAuthHarness.Nsg, "race", null).GetAwaiter().GetResult();
        int tokensBefore = h.Auth.Tokens.MobileTokensIssued;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.LoginAsync(Login(), Ip);

        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, result.ErrorCode);
        Assert.Equal(tokensBefore, h.Auth.Tokens.MobileTokensIssued);
        Assert.Empty(h.Auth.Sessions.Tokens);
        Assert.True(h.Profile.IsAdminBlocked);
    }

    [Fact]
    public async Task A_profile_deactivated_for_another_reason_keeps_its_old_login_answer()
    {
        MobileAccessHarness h = new();
        h.Profile.IsActive = false;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.LoginAsync(Login(), Ip);

        AssertNotTheBlockCode(result);
        Assert.Equal(ApiErrorCodes.Forbidden, result.ErrorCode);
        Assert.Equal(MobileAuthMessages.AccessUnavailable, result.ErrorMessage);
    }

    [Fact]
    public async Task Restore_allows_a_fresh_login()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        await h.Service.RestoreAsync(MobileAuthHarness.Nsg, null, null);

        Result<MobileLoginResponseDto> result = await h.Auth.Service.LoginAsync(Login(), Ip);

        Assert.True(result.IsSuccess);
        Assert.Single(h.Auth.Sessions.Tokens, t => t.RevokedAtIst == null);
    }

    // ───── login/check ─────

    [Fact]
    public async Task Login_check_of_a_blocked_profile_keeps_the_unavailable_status_and_adds_the_code()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);

        Result<MobileAuthFlowDto> result = await h.Auth.Service.CheckAsync(h.Auth.Check());

        Assert.True(result.IsSuccess);
        Assert.Equal(MobileAuthStatus.AccessUnavailable, result.Data!.Status);
        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, result.Data.Code);
        Assert.Equal(MobileAuthMessages.AccessBlockedByPisAdmin, result.Data.Message);
    }

    [Fact]
    public async Task Login_check_never_carries_the_code_for_anything_else()
    {
        MobileAccessHarness active = new();
        var ok = await active.Auth.Service.CheckAsync(active.Auth.Check());
        Assert.Equal(MobileAuthStatus.MpinRequired, ok.Data!.Status);
        Assert.Null(ok.Data.Code);

        MobileAccessHarness inactive = new();
        inactive.Profile.IsActive = false;
        var unrelated = await inactive.Auth.Service.CheckAsync(inactive.Auth.Check());
        Assert.Equal(MobileAuthStatus.AccessUnavailable, unrelated.Data!.Status);
        Assert.Null(unrelated.Data.Code);
        Assert.Equal(MobileAuthMessages.AccessUnavailable, unrelated.Data.Message);

        MobileAccessHarness none = new(withProfile: false);
        none.Auth.Pis.Rows.Clear();
        var unknown = await none.Auth.Service.CheckAsync(none.Auth.Check());
        Assert.False(unknown.IsSuccess);
        Assert.NotEqual(ApiErrorCodes.MobileAccessBlocked, unknown.ErrorCode);

        // Someone who does not know the mobile number and date of birth learns nothing about the block.
        MobileAccessHarness blocked = new();
        await blocked.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        var wrongIdentity = await blocked.Auth.Service.CheckAsync(blocked.Auth.Check(mobile: "9111111111"));
        AssertNotTheBlockCode(wrongIdentity);
    }

    // ───── refresh ─────

    [Fact]
    public async Task Refresh_of_a_blocked_session_never_issues_tokens_and_returns_the_code()
    {
        MobileAccessHarness h = new();
        MobileLoginResponseDto session = await h.LoginAsync();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        int tokensBefore = h.Auth.Tokens.MobileTokensIssued;
        int rowsBefore = h.Auth.Sessions.Tokens.Count;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.RefreshAsync(Refresh(session.RefreshToken), Ip);

        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, result.ErrorCode);
        Assert.Equal(MobileAuthMessages.AccessBlockedByPisAdmin, result.ErrorMessage);
        Assert.Equal(tokensBefore, h.Auth.Tokens.MobileTokensIssued);
        Assert.Equal(rowsBefore, h.Auth.Sessions.Tokens.Count);
    }

    [Fact]
    public async Task Refresh_of_a_rotated_session_that_was_then_blocked_returns_the_code_for_the_current_token_only()
    {
        MobileAccessHarness h = new();
        MobileLoginResponseDto first = await h.LoginAsync();
        MobileLoginResponseDto second = (await h.Auth.Service.RefreshAsync(Refresh(first.RefreshToken), Ip)).Data!;
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);

        // The live token of the session was revoked as AdminBlocked, the earlier rotated row was not.
        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, (await h.Auth.Service.RefreshAsync(Refresh(second.RefreshToken), Ip)).ErrorCode);
        Assert.Equal(MobileSessionRevokeReasons.Rotated, h.Auth.Sessions.Tokens[0].RevokeReason);
        // Replaying the old, already rotated token is reuse detection, not a block.
        Assert.Equal(ApiErrorCodes.RefreshTokenReuseDetected, (await h.Auth.Service.RefreshAsync(Refresh(first.RefreshToken), Ip)).ErrorCode);
    }

    [Fact]
    public async Task A_block_that_lands_between_the_rotation_and_the_profile_read_leaves_no_usable_token()
    {
        MobileAccessHarness h = new();
        MobileLoginResponseDto session = await h.LoginAsync();
        // The block has committed its profile change but its revocation has not reached the live token yet.
        h.Profile.IsActive = false; h.Profile.AccessBlockedAtIst = DateTime.UtcNow; h.Profile.AccessBlockedBy = 1;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.RefreshAsync(Refresh(session.RefreshToken), Ip);

        Assert.Equal(ApiErrorCodes.MobileAccessBlocked, result.ErrorCode);
        Assert.DoesNotContain(h.Auth.Sessions.Tokens, t => t.RevokedAtIst == null);
        Assert.Equal(MobileSessionRevokeReasons.AdminBlocked, h.Auth.Sessions.Tokens[^1].RevokeReason);   // not relabelled ProfileInactive
    }

    [Fact]
    public async Task After_a_restore_the_old_refresh_token_still_does_not_work_and_issues_nothing()
    {
        MobileAccessHarness h = new();
        MobileLoginResponseDto old = await h.LoginAsync();
        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        await h.Service.RestoreAsync(MobileAuthHarness.Nsg, null, null);
        int rowsBefore = h.Auth.Sessions.Tokens.Count;

        Result<MobileLoginResponseDto> result = await h.Auth.Service.RefreshAsync(Refresh(old.RefreshToken), Ip);

        Assert.False(result.IsSuccess);
        Assert.Equal(rowsBefore, h.Auth.Sessions.Tokens.Count);
        Assert.DoesNotContain(h.Auth.Sessions.Tokens, t => t.RevokedAtIst == null);
    }

    // ───── access-token session state ─────

    [Fact]
    public async Task The_session_check_reports_a_block_only_for_a_block()
    {
        MobileAccessHarness h = new();
        await h.LoginAsync();
        Guid sid = h.Auth.Tokens.LastSessionId;
        long id = h.Profile.Id;
        Assert.Equal(MobileSessionState.Active, await h.Auth.Sessions.GetSessionStateAsync(sid, id));

        await h.Service.BlockAsync(MobileAuthHarness.Nsg, null, null);
        Assert.Equal(MobileSessionState.AdminBlocked, await h.Auth.Sessions.GetSessionStateAsync(sid, id));
    }

    [Fact]
    public async Task A_live_token_on_a_blocked_profile_is_a_block_because_the_profile_marker_is_authoritative()
    {
        MobileAccessHarness h = new();
        await h.LoginAsync();
        Guid sid = h.Auth.Tokens.LastSessionId;
        h.Profile.IsActive = false; h.Profile.AccessBlockedAtIst = DateTime.UtcNow;      // marker set, token not yet revoked

        Assert.Equal(MobileSessionState.AdminBlocked, await h.Auth.Sessions.GetSessionStateAsync(sid, h.Profile.Id));
    }

    [Fact]
    public async Task Every_other_way_a_session_ends_is_plain_revoked_never_a_block()
    {
        // logout
        MobileAccessHarness logout = new();
        MobileLoginResponseDto a = await logout.LoginAsync();
        Guid sidA = logout.Auth.Tokens.LastSessionId;
        await logout.Auth.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = a.RefreshToken }, Ip);
        Assert.Equal(MobileSessionState.Revoked, await logout.Auth.Sessions.GetSessionStateAsync(sidA, logout.Profile.Id));

        // new login on the same profile
        MobileAccessHarness replaced = new();
        await replaced.LoginAsync();
        Guid oldSid = replaced.Auth.Tokens.LastSessionId;
        await replaced.LoginAsync();
        Assert.Equal(MobileSessionState.Replaced, await replaced.Auth.Sessions.GetSessionStateAsync(oldSid, replaced.Profile.Id));   // SURAKSHA_1002, never 1001

        // MPIN change / reset
        MobileAccessHarness mpin = new();
        await mpin.LoginAsync();
        Guid sidM = mpin.Auth.Tokens.LastSessionId;
        await mpin.Auth.Profiles.SetMpinAsync(mpin.Profile.Id, "hash:1357", DateTime.UtcNow);
        Assert.Equal(MobileSessionState.Revoked, await mpin.Auth.Sessions.GetSessionStateAsync(sidM, mpin.Profile.Id));

        // profile deactivated for another reason
        MobileAccessHarness inactive = new();
        await inactive.LoginAsync();
        Guid sidI = inactive.Auth.Tokens.LastSessionId;
        inactive.Profile.IsActive = false;
        Assert.Equal(MobileSessionState.Revoked, await inactive.Auth.Sessions.GetSessionStateAsync(sidI, inactive.Profile.Id));

        // unknown session
        Assert.Equal(MobileSessionState.Revoked, await inactive.Auth.Sessions.GetSessionStateAsync(Guid.NewGuid(), inactive.Profile.Id));
    }

    // ───── no other situation ever returns the code ─────

    [Fact]
    public async Task Logout_expiry_invalid_reuse_new_login_mpin_change_inactive_and_lockout_never_return_the_code()
    {
        // normal logout, then the token is used
        MobileAccessHarness h = new();
        MobileLoginResponseDto s = await h.LoginAsync();
        await h.Auth.Service.LogoutAsync(new MobileLogoutRequestDto { RefreshToken = s.RefreshToken }, Ip);
        Result<MobileLoginResponseDto> afterLogout = await h.Auth.Service.RefreshAsync(Refresh(s.RefreshToken), Ip);
        AssertNotTheBlockCode(afterLogout);
        Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, afterLogout.ErrorCode);

        // unknown token
        Result<MobileLoginResponseDto> unknown = await h.Auth.Service.RefreshAsync(Refresh("no-such-token"), Ip);
        AssertNotTheBlockCode(unknown);
        Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, unknown.ErrorCode);

        // expiry
        MobileAccessHarness expiry = new();
        MobileLoginResponseDto e = await expiry.LoginAsync();
        expiry.Auth.Clock.Advance(TimeSpan.FromDays(8));
        Result<MobileLoginResponseDto> expired = await expiry.Auth.Service.RefreshAsync(Refresh(e.RefreshToken), Ip);
        AssertNotTheBlockCode(expired);
        Assert.Equal(ApiErrorCodes.RefreshTokenExpired, expired.ErrorCode);

        // reuse detection
        MobileAccessHarness reuse = new();
        MobileLoginResponseDto r = await reuse.LoginAsync();
        await reuse.Auth.Service.RefreshAsync(Refresh(r.RefreshToken), Ip);
        Result<MobileLoginResponseDto> replay = await reuse.Auth.Service.RefreshAsync(Refresh(r.RefreshToken), Ip);
        AssertNotTheBlockCode(replay);
        Assert.Equal(ApiErrorCodes.RefreshTokenReuseDetected, replay.ErrorCode);

        // replaced by a new login
        MobileAccessHarness replaced = new();
        MobileLoginResponseDto first = await replaced.LoginAsync();
        await replaced.LoginAsync();
        Result<MobileLoginResponseDto> afterReplace = await replaced.Auth.Service.RefreshAsync(Refresh(first.RefreshToken), Ip);
        AssertNotTheBlockCode(afterReplace);
        Assert.Equal(ApiErrorCodes.MobileSessionReplaced, afterReplace.ErrorCode);   // its own code (see MobileSessionReplacementTests)

        // MPIN change and MPIN reset end sessions with their own reasons
        foreach (string reason in new[] { MobileSessionRevokeReasons.MpinChanged, MobileSessionRevokeReasons.MpinReset })
        {
            MobileAccessHarness m = new();
            MobileLoginResponseDto ms = await m.LoginAsync();
            m.Auth.Sessions.RevokeForMpinChange(m.Profile.Id, reason, DateTime.UtcNow);
            Result<MobileLoginResponseDto> afterMpin = await m.Auth.Service.RefreshAsync(Refresh(ms.RefreshToken), Ip);
            AssertNotTheBlockCode(afterMpin);
            Assert.Equal(ApiErrorCodes.RefreshTokenInvalid, afterMpin.ErrorCode);
        }

        // profile inactive for another reason: refresh
        MobileAccessHarness inactive = new();
        MobileLoginResponseDto i = await inactive.LoginAsync();
        inactive.Profile.IsActive = false;
        Result<MobileLoginResponseDto> inactiveRefresh = await inactive.Auth.Service.RefreshAsync(Refresh(i.RefreshToken), Ip);
        AssertNotTheBlockCode(inactiveRefresh);
        Assert.Equal(ApiErrorCodes.Forbidden, inactiveRefresh.ErrorCode);
        Assert.Equal(MobileSessionRevokeReasons.ProfileInactive, inactive.Auth.Sessions.Tokens[^1].RevokeReason);

        // lockout
        MobileAccessHarness locked = new();
        for (int n = 0; n < locked.Auth.Settings.MaxFailedAttempts; n++) await locked.Auth.Service.LoginAsync(Login("0000"), Ip);
        Result<MobileLoginResponseDto> lockedLogin = await locked.Auth.Service.LoginAsync(Login(), Ip);
        AssertNotTheBlockCode(lockedLogin);
        Assert.Equal(ApiErrorCodes.AccountLocked, lockedLogin.ErrorCode);
    }
}
