using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>Workflow tests over in-memory fakes (no database): login/check order, OTP, grants, MPIN, lockout, approval decisions.</summary>
public class MobileAuthServiceTests
{
    private const string Ip = "127.0.0.1";

    // ───── login/check ─────

    [Fact]
    public async Task Check_active_profile_requires_mpin_without_querying_pis()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        var r = await h.Service.CheckAsync(h.Check());
        Assert.Equal(MobileAuthStatus.MpinRequired, r.Data!.Status);
        Assert.Equal(0, h.Pis.NsgLookups);
    }

    [Fact]
    public async Task Check_inactive_profile_directs_to_it_support()
    {
        MobileAuthHarness h = new(); h.AddProfile(active: false);
        var r = await h.Service.CheckAsync(h.Check());
        Assert.Equal(MobileAuthStatus.AccessUnavailable, r.Data!.Status);
        Assert.Equal("Your access is unavailable. Please contact the IT Admin.", r.Data.Message);
    }

    [Fact]
    public async Task Check_profile_with_other_dob_is_a_generic_no_match()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        var r = await h.Service.CheckAsync(h.Check(dob: new DateOnly(1991, 1, 1)));
        Assert.False(r.IsSuccess);
        Assert.Equal(ApiErrorCodes.MobileNotMatched, r.ErrorCode);
        Assert.Equal(0, h.Pis.NsgLookups);
    }

    [Fact]
    public async Task Check_pending_request_skips_pis_and_otp()
    {
        MobileAuthHarness h = new();
        await h.Requests.AddPendingAsync(new ApproveMobileUser { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob });
        var r = await h.Service.CheckAsync(h.Check());
        Assert.Equal(MobileAuthStatus.RequestPending, r.Data!.Status);
        Assert.Equal("Your request is already pending. Please wait for approval.", r.Data.Message);
        Assert.Equal(0, h.Pis.NsgLookups);

        var send = await h.Service.SendOtpAsync(h.Send(), Ip);
        Assert.Equal(MobileAuthStatus.RequestPending, send.Data!.Status);
        Assert.Equal(0, h.Otp.Sends);
        Assert.Single(h.Requests.Requests);
    }

    [Fact]
    public async Task Check_pending_request_is_not_acknowledged_to_a_caller_who_does_not_know_the_mobile()
    {
        MobileAuthHarness h = new();
        await h.Requests.AddPendingAsync(new ApproveMobileUser { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob });
        var r = await h.Service.CheckAsync(h.Check(mobile: "9123456780"));
        Assert.Equal(ApiErrorCodes.MobileNotMatched, r.ErrorCode);
        Assert.Equal(0, h.Pis.NsgLookups);
    }

    [Theory]
    [InlineData(MobileAppActivationStatus.Pending, MobileAuthStatus.OtpRequired)]
    [InlineData(MobileAppActivationStatus.Approved, MobileAuthStatus.OtpRequired)]
    [InlineData(MobileAppActivationStatus.Rejected, MobileAuthStatus.AccessUnavailable)]
    public async Task Check_matches_pis_and_evaluates_activation_status(MobileAppActivationStatus status, string expected)
    {
        MobileAuthHarness h = new(status);
        var r = await h.Service.CheckAsync(h.Check());
        Assert.Equal(expected, r.Data!.Status);
    }

    [Theory]
    [InlineData("NSG-9999", null, null)]
    [InlineData(null, "9123456780", null)]
    public async Task Check_requires_all_three_fields_to_match_pis(string? nsg, string? mobile, string? _)
    {
        MobileAuthHarness h = new();
        var r = await h.Service.CheckAsync(h.Check(nsg, mobile));
        Assert.Equal(ApiErrorCodes.MobileNotMatched, r.ErrorCode);
    }

    [Fact]
    public async Task Check_pis_dob_mismatch_and_inactive_pis()
    {
        MobileAuthHarness h = new();
        Assert.Equal(ApiErrorCodes.MobileNotMatched, (await h.Service.CheckAsync(h.Check(dob: new DateOnly(1990, 5, 16)))).ErrorCode);
        h.Pis.Rows[0].IsActive = false;
        Assert.Equal(MobileAuthStatus.AccessUnavailable, (await h.Service.CheckAsync(h.Check())).Data!.Status);
    }

    [Fact]
    public async Task Check_accepts_plus91_and_surrounding_spaces()
    {
        MobileAuthHarness h = new();
        var r = await h.Service.CheckAsync(h.Check(nsg: "  nsg-1001 ", mobile: "+91 98765 43210"));
        Assert.Equal(MobileAuthStatus.OtpRequired, r.Data!.Status);
    }

    // ───── login ─────

    private static MobileLoginRequestDto Login(string mpin) => new() { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = mpin };

    [Fact]
    public async Task Login_with_correct_mpin_issues_token_and_resets_counter()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile();
        await h.Service.LoginAsync(Login("0000"), Ip);
        var r = await h.Service.LoginAsync(Login(MobileAuthHarness.Mpin), Ip);
        Assert.True(r.IsSuccess);
        Assert.Equal("jwt-for-" + p.Id, r.Data!.AccessToken);
        Assert.Equal("Employee", r.Data.Profile.Role);
        Assert.Equal(0, p.FailedLoginAttempts);
        Assert.NotNull(p.LastLoginAtIst);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.LoginSuccess);
    }

    [Fact]
    public async Task Login_failures_never_issue_a_token()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        var wrong = await h.Service.LoginAsync(Login("0000"), Ip);
        var unknown = await h.Service.LoginAsync(new MobileLoginRequestDto { NsgNumber = "X-1", MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = "2580" }, Ip);
        Assert.Equal(ApiErrorCodes.InvalidCredentials, wrong.ErrorCode);
        Assert.Equal(ApiErrorCodes.InvalidCredentials, unknown.ErrorCode);
        Assert.Equal(0, h.Tokens.MobileTokensIssued);
    }

    [Fact]
    public async Task Login_locks_after_max_attempts_and_unlocks_after_the_lockout()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile();
        await h.Service.LoginAsync(Login("0000"), Ip);
        await h.Service.LoginAsync(Login("0000"), Ip);
        var third = await h.Service.LoginAsync(Login("0000"), Ip);
        Assert.Equal(ApiErrorCodes.AccountLocked, third.ErrorCode);
        Assert.Equal(IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddMinutes(30), p.LockedUntilIst);

        var correctWhileLocked = await h.Service.LoginAsync(Login(MobileAuthHarness.Mpin), Ip);
        Assert.Equal(ApiErrorCodes.AccountLocked, correctWhileLocked.ErrorCode);
        Assert.Equal(0, h.Tokens.MobileTokensIssued);

        h.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True((await h.Service.LoginAsync(Login(MobileAuthHarness.Mpin), Ip)).IsSuccess);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.AccountLocked);
    }

    [Fact]
    public async Task Login_after_lockout_expiry_gets_a_fresh_attempt_budget()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile();
        for (int i = 0; i < 3; i++) await h.Service.LoginAsync(Login("0000"), Ip);
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        var r = await h.Service.LoginAsync(Login("0000"), Ip);
        Assert.Equal(ApiErrorCodes.InvalidCredentials, r.ErrorCode);   // one wrong guess, not an instant relock
        Assert.Equal(1, p.FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_inactive_profile_is_refused_with_the_it_support_message()
    {
        MobileAuthHarness h = new(); h.AddProfile(active: false);
        var r = await h.Service.LoginAsync(Login(MobileAuthHarness.Mpin), Ip);
        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        Assert.Equal(MobileAuthMessages.AccessUnavailable, r.ErrorMessage);
        Assert.Equal(0, h.Tokens.MobileTokensIssued);
    }

    [Fact]
    public async Task Login_mismatched_identifiers_do_not_count_against_the_real_profile()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile();
        for (int i = 0; i < 5; i++)
            await h.Service.LoginAsync(new MobileLoginRequestDto { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = "9123456780", DateOfBirth = MobileAuthHarness.Dob, Mpin = "0000" }, Ip);
        Assert.Equal(0, p.FailedLoginAttempts);
        Assert.Null(p.LockedUntilIst);
    }

    // ───── otp/send ─────

    [Fact]
    public async Task Send_otp_goes_to_the_registered_number_only()
    {
        MobileAuthHarness h = new();
        var r = await h.Service.SendOtpAsync(h.Send(), Ip);
        Assert.Equal(MobileAuthStatus.OtpSent, r.Data!.Status);
        Assert.Equal("+91" + MobileAuthHarness.Mobile, h.Otp.Requests.Single().MobileNumber);
        Assert.Equal("XXXXXX3210", r.Data.MaskedMobileNumber);
        Assert.Equal(TimeSpan.FromHours(5.5), r.Data.OtpExpiresAt!.Value.Offset);
        Assert.Equal(h.Clock.Now.UtcDateTime.AddMinutes(5), r.Data.OtpExpiresAt.Value.UtcDateTime);
    }

    [Fact]
    public async Task Send_otp_is_refused_for_unmatched_people_and_active_profiles_and_rejected_status()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Rejected);
        Assert.Equal(MobileAuthStatus.AccessUnavailable, (await h.Service.SendOtpAsync(h.Send(), Ip)).Data!.Status);

        MobileAuthHarness h2 = new();
        Assert.Equal(ApiErrorCodes.MobileNotMatched, (await h2.Service.SendOtpAsync(new MobileOtpSendRequestDto { NsgNumber = "none", MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob }, Ip)).ErrorCode);

        MobileAuthHarness h3 = new(); h3.AddProfile();
        Assert.Equal(MobileAuthStatus.MpinRequired, (await h3.Service.SendOtpAsync(h3.Send(), Ip)).Data!.Status);
        Assert.Equal(0, h.Otp.Sends + h2.Otp.Sends + h3.Otp.Sends);
    }

    [Fact]
    public async Task Send_otp_enforces_resend_cooldown_and_hourly_cap()
    {
        MobileAuthHarness h = new();
        Assert.True((await h.Service.SendOtpAsync(h.Send(), Ip)).IsSuccess);
        Assert.Equal(ApiErrorCodes.OtpResendCooldown, (await h.Service.SendOtpAsync(h.Send(), Ip)).ErrorCode);

        for (int i = 0; i < 2; i++) { h.Clock.Advance(TimeSpan.FromSeconds(61)); Assert.True((await h.Service.SendOtpAsync(h.Send(), Ip)).IsSuccess); }
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(ApiErrorCodes.OtpRateLimited, (await h.Service.SendOtpAsync(h.Send(), Ip)).ErrorCode);
        Assert.Equal(3, h.Otp.Sends);

        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await h.Service.SendOtpAsync(h.Send(), Ip)).IsSuccess);
    }

    [Fact]
    public async Task Send_otp_sms_failure_is_reported()
    {
        MobileAuthHarness h = new(); h.Otp.SmsFails = true;
        Assert.Equal(ApiErrorCodes.OtpSendFailed, (await h.Service.SendOtpAsync(h.Send(), Ip)).ErrorCode);
    }

    // ───── otp/verify ─────

    [Fact]
    public async Task Verify_pending_pis_creates_exactly_one_approval_request_only_after_a_correct_otp()
    {
        MobileAuthHarness h = new();
        var sent = await h.Service.SendOtpAsync(h.Send(), Ip);
        var wrong = await h.Service.VerifyOtpAsync(h.Verify(sent.Data!.OtpReferenceId!.Value, "000000"), Ip);
        Assert.Equal(ApiErrorCodes.OtpInvalid, wrong.ErrorCode);
        Assert.Empty(h.Requests.Requests);

        var ok = await h.Service.VerifyOtpAsync(h.Verify(sent.Data.OtpReferenceId.Value), Ip);
        Assert.Equal(MobileAuthStatus.ApprovalRequested, ok.Data!.Status);
        Assert.Equal("Your approval request has been raised successfully.", ok.Data.Message);
        ApproveMobileUser request = Assert.Single(h.Requests.Requests);
        Assert.Equal(MobileAppActivationStatus.Pending, request.ApprovalStatus);
        Assert.Equal(MobileAuthHarness.Mobile, request.MobileNumber);
        Assert.Empty(h.Profiles.Profiles);
        Assert.Null(ok.Data.GrantToken);

        // A second attempt now hits the "already pending" rule: no new OTP, no new request.
        var again = await h.Service.SendOtpAsync(h.Send(), Ip);
        Assert.Equal(MobileAuthStatus.RequestPending, again.Data!.Status);
        Assert.Single(h.Requests.Requests);
    }

    [Fact]
    public async Task Verify_approved_pis_issues_a_grant_and_no_approval_request()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        var r = await h.SendAndVerifyAsync();
        Assert.Equal(MobileAuthStatus.RegistrationVerified, r.Data!.Status);
        Assert.False(string.IsNullOrEmpty(r.Data.GrantToken));
        Assert.Empty(h.Requests.Requests);
        MobileAuthGrant grant = Assert.Single(h.Grants.Grants);
        Assert.NotEqual(r.Data.GrantToken, grant.TokenHash);   // only the hash is stored
        Assert.Equal(MobileGrantPurpose.RegistrationVerified, grant.Purpose);
        Assert.Equal(IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddMinutes(10), grant.ExpiresAtIst);
    }

    [Fact]
    public async Task Verify_rejects_expired_and_over_attempted_otps_and_foreign_references()
    {
        MobileAuthHarness h = new();
        var sent = await h.Service.SendOtpAsync(h.Send(), Ip);
        Guid reference = sent.Data!.OtpReferenceId!.Value;

        for (int i = 0; i < 5; i++) await h.Service.VerifyOtpAsync(h.Verify(reference, "000000"), Ip);
        Assert.Equal(ApiErrorCodes.OtpMaxAttemptsExceeded, (await h.Service.VerifyOtpAsync(h.Verify(reference), Ip)).ErrorCode);

        MobileAuthHarness h2 = new();
        var sent2 = await h2.Service.SendOtpAsync(h2.Send(), Ip);
        h2.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(ApiErrorCodes.OtpExpired, (await h2.Service.VerifyOtpAsync(h2.Verify(sent2.Data!.OtpReferenceId!.Value), Ip)).ErrorCode);

        Assert.Equal(ApiErrorCodes.OtpInvalid, (await h2.Service.VerifyOtpAsync(h2.Verify(Guid.NewGuid()), Ip)).ErrorCode);
        Assert.Empty(h.Requests.Requests);
    }

    [Fact]
    public async Task Verify_rejects_an_otp_that_was_sent_to_somebody_elses_number()
    {
        MobileAuthHarness h = new();
        h.Otp.Requests.Add(new OtpRequest { MobileNumber = "+919123456780", Purpose = OtpPurpose.Registration, OtpHash = FakeOtp.Code, ExpiresAt = IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddMinutes(5), CreatedAt = IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime) });
        var r = await h.Service.VerifyOtpAsync(h.Verify(h.Otp.Requests[0].ReferenceId), Ip);
        Assert.Equal(ApiErrorCodes.OtpInvalid, r.ErrorCode);
        Assert.Empty(h.Requests.Requests);
    }

    [Fact]
    public async Task Verify_otp_is_single_use()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        var sent = await h.Service.SendOtpAsync(h.Send(), Ip);
        Guid reference = sent.Data!.OtpReferenceId!.Value;
        Assert.True((await h.Service.VerifyOtpAsync(h.Verify(reference), Ip)).IsSuccess);
        Assert.False((await h.Service.VerifyOtpAsync(h.Verify(reference), Ip)).IsSuccess);
    }

    [Fact]
    public async Task Verify_revalidates_eligibility_after_the_otp()
    {
        MobileAuthHarness h = new();
        h.Otp.DuringVerify = () => h.Pis.Rows[0].MobileAppActivationStatus = MobileAppActivationStatus.Rejected;
        var r = await h.SendAndVerifyAsync();
        Assert.Equal(MobileAuthStatus.AccessUnavailable, r.Data!.Status);
        Assert.Empty(h.Requests.Requests);
    }

    // ───── registration/submit ─────

    private static async Task<string> ApprovedGrantAsync(MobileAuthHarness h)
        => (await h.SendAndVerifyAsync()).Data!.GrantToken!;

    [Fact]
    public async Task Submit_for_an_approved_person_returns_the_mpin_setup_grant_without_an_approval_request()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await ApprovedGrantAsync(h);
        var r = await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip);
        Assert.Equal(MobileAuthStatus.MpinSetupGranted, r.Data!.Status);
        Assert.NotNull(r.Data.GrantToken);
        Assert.Empty(h.Requests.Requests);
        Assert.Empty(h.Profiles.Profiles);
        Assert.Contains(h.Grants.Grants, g => g.Purpose == MobileGrantPurpose.MpinSetup && g.ConsumedAtIst == null);
    }

    [Fact]
    public async Task Submit_grant_is_single_use_expires_and_has_a_purpose()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await ApprovedGrantAsync(h);
        var first = await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip);
        Assert.True(first.IsSuccess);
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip)).ErrorCode);

        // The MPIN-setup grant is not a registration grant.
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = first.Data!.GrantToken! }, Ip)).ErrorCode);
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = "made-up" }, Ip)).ErrorCode);

        MobileAuthHarness h2 = new(MobileAppActivationStatus.Approved);
        string token2 = await ApprovedGrantAsync(h2);
        h2.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h2.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token2 }, Ip)).ErrorCode);
    }

    [Fact]
    public async Task Submit_rechecks_pis_status()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await ApprovedGrantAsync(h);
        h.Pis.Rows[0].MobileAppActivationStatus = MobileAppActivationStatus.Rejected;
        var r = await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip);
        Assert.Equal(MobileAuthStatus.AccessUnavailable, r.Data!.Status);
        Assert.Empty(h.Profiles.Profiles);
    }

    [Fact]
    public async Task Submit_when_status_went_back_to_pending_raises_one_request_and_creates_no_profile()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await ApprovedGrantAsync(h);
        h.Pis.Rows[0].MobileAppActivationStatus = MobileAppActivationStatus.Pending;
        var r = await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip);
        Assert.Equal(MobileAuthStatus.ApprovalRequested, r.Data!.Status);
        Assert.Single(h.Requests.Requests);
        Assert.Empty(h.Profiles.Profiles);
    }

    [Fact]
    public async Task Submit_does_not_duplicate_an_existing_pending_request()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await ApprovedGrantAsync(h);
        await h.Requests.AddPendingAsync(new ApproveMobileUser { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob });
        var r = await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = token }, Ip);
        Assert.Equal(MobileAuthStatus.RequestPending, r.Data!.Status);
        Assert.Single(h.Requests.Requests);
    }

    // ───── mpin/setup ─────

    private static async Task<string> SetupGrantAsync(MobileAuthHarness h)
    {
        string registration = await ApprovedGrantAsync(h);
        return (await h.Service.SubmitRegistrationAsync(new RegistrationSubmitRequestDto { GrantToken = registration }, Ip)).Data!.GrantToken!;
    }

    [Fact]
    public async Task Setup_creates_an_employee_profile_from_pis_with_a_hashed_mpin_and_consumes_the_grant()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string token = await SetupGrantAsync(h);
        var r = await h.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = token, Mpin = "2580", ConfirmMpin = "2580" }, Ip);

        Assert.Equal(MobileAuthStatus.ProfileCreated, r.Data!.Status);
        MobileUserProfile p = Assert.Single(h.Profiles.Profiles);
        Assert.Equal(UserRole.Employee, p.Role);
        Assert.True(p.IsActive);
        Assert.Equal(MobileAuthHarness.Nsg, p.NsgNumber);
        Assert.Equal(MobileAuthHarness.Dob, p.DateOfBirth);
        Assert.NotEqual("2580", p.MpinHash);
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = token, Mpin = "2580", ConfirmMpin = "2580" }, Ip)).ErrorCode);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.MobileProfileCreated);
    }

    [Fact]
    public async Task Setup_refuses_a_registration_grant_and_a_person_who_is_no_longer_approved()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string registration = await ApprovedGrantAsync(h);
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = registration, Mpin = "2580", ConfirmMpin = "2580" }, Ip)).ErrorCode);

        MobileAuthHarness h2 = new(MobileAppActivationStatus.Approved);
        string setup = await SetupGrantAsync(h2);
        h2.Pis.Rows[0].MobileAppActivationStatus = MobileAppActivationStatus.Rejected;
        var r = await h2.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = setup, Mpin = "2580", ConfirmMpin = "2580" }, Ip);
        Assert.Equal(MobileAuthStatus.AccessUnavailable, r.Data!.Status);
        Assert.Empty(h2.Profiles.Profiles);
    }

    [Fact]
    public async Task Setup_cannot_create_a_duplicate_profile()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string setup = await SetupGrantAsync(h);
        h.AddProfile();   // somebody got there first
        var r = await h.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = setup, Mpin = "2580", ConfirmMpin = "2580" }, Ip);
        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        Assert.Single(h.Profiles.Profiles);
    }

    // ───── mpin/change ─────

    [Fact]
    public async Task Change_requires_an_authenticated_profile()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        var r = await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = "2580", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        Assert.Equal(ApiErrorCodes.Unauthorized, r.ErrorCode);
    }

    [Fact]
    public async Task Change_verifies_the_current_mpin_and_counts_wrong_attempts()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile(); h.CurrentUser.ProfileId = p.Id;
        var wrong = await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = "0000", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        Assert.Equal(ApiErrorCodes.InvalidCredentials, wrong.ErrorCode);
        Assert.Equal(1, p.FailedLoginAttempts);
        Assert.Equal("hash:2580", p.MpinHash);

        var ok = await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = "2580", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        Assert.Equal(MobileAuthStatus.MpinChanged, ok.Data!.Status);
        Assert.Equal("hash:1357", p.MpinHash);
        Assert.Equal(0, p.FailedLoginAttempts);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditAction.MpinChanged);
    }

    [Fact]
    public async Task Change_stops_when_locked()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile(); h.CurrentUser.ProfileId = p.Id;
        for (int i = 0; i < 3; i++) await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = "0000", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        var r = await h.Service.ChangeMpinAsync(new MpinChangeRequestDto { CurrentMpin = "2580", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        Assert.Equal(ApiErrorCodes.AccountLocked, r.ErrorCode);
        Assert.Equal("hash:2580", p.MpinHash);
    }

    // ───── mpin/reset ─────

    [Fact]
    public async Task Reset_needs_a_verified_otp_and_cannot_be_done_with_identifiers_alone()
    {
        MobileAuthHarness h = new(); h.AddProfile();
        // Identifiers alone: no OTP sent, no grant, and an invented grant is useless.
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.ResetMpinAsync(new MpinResetRequestDto { GrantToken = "guess", NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip)).ErrorCode);

        var wrongOtp = await h.Service.SendOtpAsync(h.Send(MobileOtpFlow.MpinReset), Ip);
        var bad = await h.Service.VerifyOtpAsync(h.Verify(wrongOtp.Data!.OtpReferenceId!.Value, "000000", MobileOtpFlow.MpinReset), Ip);
        Assert.False(bad.IsSuccess);
        Assert.DoesNotContain(h.Grants.Grants, g => g.Purpose == MobileGrantPurpose.MpinReset);
    }

    [Fact]
    public async Task Reset_flow_sets_the_mpin_unlocks_and_invalidates_other_grants()
    {
        MobileAuthHarness h = new(); MobileUserProfile p = h.AddProfile();
        p.FailedLoginAttempts = 3; p.LockedUntilIst = IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddMinutes(20);
        h.Grants.Grants.Add(new MobileAuthGrant { Id = 99, TokenHash = "other", Purpose = MobileGrantPurpose.RegistrationVerified, NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, ExpiresAtIst = IndianStandardTime.ConvertFromUtc(h.Clock.Now.UtcDateTime).AddMinutes(5) });

        var verified = await h.SendAndVerifyAsync(MobileOtpFlow.MpinReset);
        Assert.Equal(MobileAuthStatus.MpinResetGranted, verified.Data!.Status);
        Assert.Equal(OtpPurpose.PasswordReset, h.Otp.Requests.Single().Purpose);

        var reset = await h.Service.ResetMpinAsync(new MpinResetRequestDto { GrantToken = verified.Data.GrantToken!, NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip);
        Assert.Equal(MobileAuthStatus.MpinReset, reset.Data!.Status);
        Assert.Equal("hash:1357", p.MpinHash);
        Assert.Equal(0, p.FailedLoginAttempts);
        Assert.Null(p.LockedUntilIst);
        Assert.All(h.Grants.Grants, g => Assert.NotNull(g.ConsumedAtIst));
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.ResetMpinAsync(new MpinResetRequestDto { GrantToken = verified.Data.GrantToken!, NewMpin = "2468", ConfirmNewMpin = "2468" }, Ip)).ErrorCode);
        Assert.True((await h.Service.LoginAsync(Login("1357"), Ip)).IsSuccess);
    }

    [Fact]
    public async Task Reset_otp_is_refused_for_people_without_a_profile_and_a_registration_grant_cannot_reset()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        var send = await h.Service.SendOtpAsync(h.Send(MobileOtpFlow.MpinReset), Ip);
        Assert.Equal(ApiErrorCodes.MobileNotMatched, send.ErrorCode);
        Assert.Equal(0, h.Otp.Sends);

        string registration = await ApprovedGrantAsync(h);
        Assert.Equal(ApiErrorCodes.GrantInvalid, (await h.Service.ResetMpinAsync(new MpinResetRequestDto { GrantToken = registration, NewMpin = "1357", ConfirmNewMpin = "1357" }, Ip)).ErrorCode);
    }

    [Fact]
    public async Task Reset_otp_for_an_inactive_profile_points_to_it_support()
    {
        MobileAuthHarness h = new(); h.AddProfile(active: false);
        var r = await h.Service.SendOtpAsync(h.Send(MobileOtpFlow.MpinReset), Ip);
        Assert.Equal(MobileAuthStatus.AccessUnavailable, r.Data!.Status);
        Assert.Equal(0, h.Otp.Sends);
    }

    // ───── audit hygiene ─────

    [Fact]
    public async Task Audit_entries_never_contain_credentials_or_personal_identifiers()
    {
        MobileAuthHarness h = new(MobileAppActivationStatus.Approved);
        string setup = await SetupGrantAsync(h);
        await h.Service.SetupMpinAsync(new MpinSetupRequestDto { GrantToken = setup, Mpin = "2580", ConfirmMpin = "2580" }, Ip);
        await h.Service.LoginAsync(Login("0000"), Ip);
        foreach (var e in h.Audit.Entries)
        {
            string text = e.EntityId + e.Data;
            Assert.DoesNotContain("2580", text);
            Assert.DoesNotContain(FakeOtp.Code, text);
            Assert.DoesNotContain(MobileAuthHarness.Mobile, text);
            Assert.DoesNotContain(MobileAuthHarness.Nsg, text);
        }
    }


    // ───── admin decisions ─────

    [Fact]
    public async Task Decision_uses_the_server_identity_and_clock_and_returns_a_message()
    {
        FakeRequestRepository repo = new();
        repo.NextDecisionResult = new ApprovalDecisionResult(ApprovalDecisionOutcome.Decided, new ApproveMobileUser
        {
            Id = 7, NsgNumber = "N", MobileNumber = "9876543210", ApprovalStatus = MobileAppActivationStatus.Approved,
            ApprovedBy = 42, ApprovedAtIst = new DateTime(2026, 10, 15, 11, 30, 0), RequestedAtIst = new DateTime(2026, 10, 14, 11, 30, 0)
        });
        TestClock clock = new();
        FakeAudit audit = new();
        FakeCurrentUser reviewerUser = new() { UserId = 42 };
        MobileApprovalService service = new(repo, audit, reviewerUser, ProductionReviewer.For(reviewerUser), clock);

        var r = await service.DecideAsync(7, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved, Remarks = "  verified  " });

        Assert.True(r.IsSuccess);
        Assert.Equal("Mobile approval request approved successfully.", r.Data!.Message);
        Assert.Equal((7L, MobileAppActivationStatus.Approved, 42, (string?)"verified", IndianStandardTime.ConvertFromUtc(clock.Now.UtcDateTime)), repo.LastDecision);
        Assert.Equal("XXXXXX3210", r.Data.Data!.MaskedMobileNumber);
        Assert.Equal(TimeSpan.FromHours(5.5), r.Data.Data.ReviewedAt!.Value.Offset);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.ITApprovalGranted && e.UserId == 42);
    }

    [Fact]
    public async Task Decision_without_a_resolvable_admin_is_refused_and_conflicts_are_reported()
    {
        FakeRequestRepository repo = new();
        FakeCurrentUser noUser = new();
        MobileApprovalService anonymous = new(repo, new FakeAudit(), noUser, ProductionReviewer.For(noUser), new TestClock());
        var r = await anonymous.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Rejected });
        Assert.Equal(ApiErrorCodes.Unauthorized, r.ErrorCode);
        Assert.Null(repo.LastDecision);

        FakeCurrentUser adminUser = new() { UserId = 1 };
        MobileApprovalService admin = new(repo, new FakeAudit(), adminUser, ProductionReviewer.For(adminUser), new TestClock());
        repo.NextDecisionResult = new ApprovalDecisionResult(ApprovalDecisionOutcome.AlreadyDecided, CurrentStatus: MobileAppActivationStatus.Approved);
        var conflict = await admin.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Rejected });
        Assert.Equal(ApiErrorCodes.Conflict, conflict.ErrorCode);
        Assert.Contains("already been approved", conflict.ErrorMessage);

        repo.NextDecisionResult = new ApprovalDecisionResult(ApprovalDecisionOutcome.NotFound);
        Assert.Equal(ApiErrorCodes.NotFound, (await admin.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved })).ErrorCode);
    }

    // ───── validators, policy, time, JWT ─────

    [Theory]
    [InlineData("2580", true)]
    [InlineData("135792", true)]
    [InlineData("1111", false)]
    [InlineData("1234", false)]
    [InlineData("9876", false)]
    [InlineData("123", false)]
    [InlineData("1234567", false)]
    [InlineData("25a0", false)]
    public void New_mpin_rules(string mpin, bool valid)
    {
        var result = new MpinSetupRequestValidator().Validate(new MpinSetupRequestDto { GrantToken = "t", Mpin = mpin, ConfirmMpin = mpin });
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Mpin_confirmation_and_change_rules()
    {
        Assert.False(new MpinSetupRequestValidator().Validate(new MpinSetupRequestDto { GrantToken = "t", Mpin = "2580", ConfirmMpin = "2581" }).IsValid);
        Assert.False(new MpinChangeRequestValidator().Validate(new MpinChangeRequestDto { CurrentMpin = "2580", NewMpin = "2580", ConfirmNewMpin = "2580" }).IsValid);
        Assert.True(new MpinChangeRequestValidator().Validate(new MpinChangeRequestDto { CurrentMpin = "2580", NewMpin = "1357", ConfirmNewMpin = "1357" }).IsValid);
    }

    [Theory]
    [InlineData("", "9876543210", false)]
    [InlineData("N1", "12345", false)]
    [InlineData("N1", "5876543210", false)]
    [InlineData("N1", "+919876543210", true)]
    [InlineData("N1", "9876543210", true)]
    public void Identity_rules(string nsg, string mobile, bool valid)
    {
        var dto = new MobileLoginCheckRequestDto { NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = new DateOnly(1990, 1, 1) };
        Assert.Equal(valid, new MobileLoginCheckRequestValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void Future_or_empty_dob_is_invalid_and_pending_is_not_a_decision()
    {
        Assert.False(new MobileLoginCheckRequestValidator().Validate(new MobileLoginCheckRequestDto { NsgNumber = "N", MobileNumber = "9876543210" }).IsValid);
        Assert.False(new MobileLoginCheckRequestValidator().Validate(new MobileLoginCheckRequestDto { NsgNumber = "N", MobileNumber = "9876543210", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) }).IsValid);
        var v = new MobileApprovalDecisionRequestValidator();
        Assert.False(v.Validate(new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Pending }).IsValid);
        Assert.True(v.Validate(new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Rejected, Remarks = "x" }).IsValid);
        Assert.False(v.Validate(new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved, Remarks = new string('x', 501) }).IsValid);
    }

    [Fact]
    public void Ist_conversion_is_utc_plus_0530()
    {
        DateTimeOffset ist = IstTime.FromUtc(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Unspecified));
        Assert.Equal(new DateTime(2026, 10, 15, 5, 30, 0), ist.DateTime);
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0), ist.UtcDateTime);
    }

    [Fact]
    public void Mobile_access_token_carries_minimal_mobile_only_claims()
    {
        JwtTokenService tokens = new(Options.Create(new JwtOptions { Secret = Convert.ToBase64String(new byte[48]), Issuer = "i", Audience = "a", AccessTokenExpirationMinutes = 15 }));
        MobileAccessToken token = tokens.GenerateMobileAccessToken(new MobileUserProfile { Id = 5, Role = UserRole.Employee, NsgNumber = "SECRET-NSG", MobileNumber = "9876543210" }, Guid.NewGuid());
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        Assert.Equal(900, token.ExpiresInSeconds);
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal("5", jwt.Claims.Single(c => c.Type == DomainConstants.MobileClaims.ProfileId).Value);
        Assert.Equal(DomainConstants.MobileClaims.MobileAccess, jwt.Claims.Single(c => c.Type == DomainConstants.MobileClaims.TokenUse).Value);
        Assert.False(int.TryParse(jwt.Subject, out _));   // never mistaken for a users.id
        Assert.DoesNotContain("SECRET-NSG", token.Token + jwt.Payload.SerializeToJson());
        Assert.DoesNotContain("9876543210", jwt.Payload.SerializeToJson());
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(15.1));
    }
}
