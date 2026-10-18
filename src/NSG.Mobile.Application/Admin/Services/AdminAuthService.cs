using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Admin.Mappings;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Admin.Services;

/// <summary>
/// Admin Portal sign-in, session refresh and sign-out. Sign-in is password then OTP (two calls, the password is checked in both);
/// the OTP always goes to the mobile number held in PIS. Tokens are issued only after both factors succeeded for an enabled,
/// activated account. A session is refreshed with its refresh token without a new OTP; signing out ends it, and the next
/// sign-in needs an OTP again.
/// </summary>
public interface IAdminAuthService
{
    Task<Result<AdminOtpSentDto>> LoginAsync(AdminLoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AdminLoginResponseDto>> VerifyLoginAsync(AdminLoginVerifyRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AdminLoginResponseDto>> RefreshAsync(AdminRefreshRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<string>> LogoutAsync(AdminLogoutRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AdminProfileSummaryDto>> GetMeAsync(long adminProfileId, CancellationToken cancellationToken = default);
}

public class AdminAuthService : IAdminAuthService
{
    // Verified against when no account matches, so "no such account" and "wrong password" cost the same time.
    private static string? s_dummyHash;

    private readonly IAdminProfileRepository _profiles;
    private readonly IAdminRefreshTokenRepository _sessions;
    private readonly IUserPisRepository _pis;
    private readonly IOtpRequestRepository _otpRequests;
    private readonly IOtpService _otpService;
    private readonly IPasswordHashingService _hasher;
    private readonly ITokenService _tokenService;
    private readonly IAdminTokenService _adminTokens;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly MobileAuthSettings _settings;
    private readonly TimeProvider _time;

    public AdminAuthService(
        IAdminProfileRepository profiles, IAdminRefreshTokenRepository sessions, IUserPisRepository pis, IOtpRequestRepository otpRequests,
        IOtpService otpService, IPasswordHashingService hasher, ITokenService tokenService, IAdminTokenService adminTokens,
        IAuditService audit, ICurrentUserService currentUser, MobileAuthSettings settings, TimeProvider time)
    {
        _profiles = profiles;
        _sessions = sessions;
        _pis = pis;
        _otpRequests = otpRequests;
        _otpService = otpService;
        _hasher = hasher;
        _tokenService = tokenService;
        _adminTokens = adminTokens;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _time = time;
    }

    private DateTime Now() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    private string DummyHash() => s_dummyHash ??= _hasher.HashPassword(Guid.NewGuid().ToString("N"));

    // ───────────────────────── sign-in step 1: password, then send OTP ─────────────────────────

    public async Task<Result<AdminOtpSentDto>> LoginAsync(AdminLoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        PasswordCheck check = await CheckPasswordAsync(request.NsgNumber, request.Password, ipAddress, cancellationToken);
        if (check.Failure != null) return Failed<AdminOtpSentDto>(check.Failure);
        AdminProfile profile = check.Profile!;

        UserPis? person = await ActivePersonnelAsync(profile, ipAddress, cancellationToken);
        if (person == null)
            return Result.Failure<AdminOtpSentDto>(ApiErrorCodes.Forbidden, AdminMessages.AccessUnavailable);

        string destination = AdminSupport.OtpDestination(person);
        int sentLastHour = await _otpRequests.CountSinceAsync(destination, OtpPurpose.AdminLogin, Now().AddHours(-1), cancellationToken);
        if (sentLastHour >= _settings.OtpMaxSendsPerHour)
            return Result.Failure<AdminOtpSentDto>(ApiErrorCodes.OtpRateLimited, AdminMessages.OtpRateLimited);

        OtpGenerationResult sent = await _otpService.GenerateAndSendAsync(destination, OtpPurpose.AdminLogin, cancellationToken);
        if (!sent.Success)
            return Result.Failure<AdminOtpSentDto>(sent.ErrorCode ?? ApiErrorCodes.OtpSendFailed, sent.ErrorMessage ?? "Failed to send OTP. Please try again.");

        await _audit.LogAsync(AuditAction.OtpRequested, DomainConstants.AuditResults.Success, entityType: nameof(OtpRequest), entityId: sent.ReferenceId?.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("flow", "AdminLogin"), ("adminProfileId", profile.Id), ("demo", sent.IsDemo)), cancellationToken: cancellationToken);

        return Result.Success(new AdminOtpSentDto
        {
            OtpReferenceId = sent.ReferenceId!.Value,
            MaskedMobileNumber = PisMasking.Mask(person.MobileNumber!),
            OtpExpiresAt = IstTime.FromIst(sent.ExpiresAtIst ?? Now()),
            DemoOtpMode = sent.IsDemo
        });
    }

    // ───────────────────────── sign-in step 2: password again + OTP, then tokens ─────────────────────────

    public async Task<Result<AdminLoginResponseDto>> VerifyLoginAsync(AdminLoginVerifyRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        PasswordCheck check = await CheckPasswordAsync(request.NsgNumber, request.Password, ipAddress, cancellationToken);
        if (check.Failure != null) return Failed<AdminLoginResponseDto>(check.Failure);
        AdminProfile profile = check.Profile!;

        UserPis? person = await ActivePersonnelAsync(profile, ipAddress, cancellationToken);
        if (person == null)
            return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.Forbidden, AdminMessages.AccessUnavailable);

        // The OTP must have been sent to this person's PIS number for sign-in.
        OtpRequest? otp = await _otpRequests.GetByReferenceIdAsync(request.OtpReferenceId, cancellationToken);
        if (otp == null || !string.Equals(otp.MobileNumber, AdminSupport.OtpDestination(person), StringComparison.Ordinal))
            return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.OtpInvalid, AdminMessages.OtpReferenceInvalid);

        OtpVerificationResult verified = await _otpService.VerifyAsync(request.OtpReferenceId, request.OtpCode, OtpPurpose.AdminLogin, cancellationToken);
        DateTime now = Now();
        if (!verified.Success)
        {
            // A wrong OTP counts like a wrong password, so a stolen password cannot be used to guess OTPs without limit.
            FailedLoginState state = await _profiles.RegisterFailedLoginAsync(profile.Id, _settings.MaxFailedAttempts, TimeSpan.FromMinutes(_settings.LockoutMinutes), now, cancellationToken);
            await _audit.LogAsync(AuditAction.OtpFailed, DomainConstants.AuditResults.Failure, entityType: nameof(OtpRequest), entityId: request.OtpReferenceId.ToString(),
                ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
                additionalData: AdminSupport.Audit(("flow", "AdminLogin"), ("errorCode", verified.ErrorCode)), cancellationToken: cancellationToken);
            return state.LockedUntilIst > now
                ? Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.AccountLocked, AdminMessages.AccountLocked)
                : Result.Failure<AdminLoginResponseDto>(verified.ErrorCode ?? ApiErrorCodes.OtpInvalid, verified.ErrorMessage ?? "OTP verification failed.");
        }

        await _profiles.RecordSuccessfulLoginAsync(profile.Id, now, cancellationToken);
        AdminLoginResponseDto session = await StartSessionAsync(profile, ipAddress, now, cancellationToken);

        await _audit.LogAsync(AuditAction.LoginSuccess, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: profile.Id.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
        return Result.Success(session);
    }

    // ───────────────────────── sessions: refresh, logout, me ─────────────────────────

    public async Task<Result<AdminLoginResponseDto>> RefreshAsync(AdminRefreshRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        DateTime now = Now();
        string nextSecret = _tokenService.GenerateRefreshToken();
        AdminRefreshToken next = NewRefreshToken(nextSecret, ipAddress, now);

        RefreshRotationResult rotation = await _sessions.RotateAsync(_tokenService.HashToken(request.RefreshToken), next, now, cancellationToken);
        switch (rotation.Outcome)
        {
            case RefreshRotationOutcome.ReuseDetected:
                await _audit.LogAsync(AuditAction.TokenReuseDetected, DomainConstants.AuditResults.Failure, entityType: nameof(AdminProfile),
                    entityId: rotation.ProfileId.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
                return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.RefreshTokenReuseDetected, AdminMessages.SessionEnded);
            case RefreshRotationOutcome.Expired:
                return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.RefreshTokenExpired, AdminMessages.SessionExpired);
            case RefreshRotationOutcome.Invalid:
                return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.RefreshTokenInvalid, AdminMessages.SessionInvalid);
        }

        // The account must still be allowed in; a deactivated one loses every session at its next refresh.
        AdminProfile? profile = await _profiles.GetByIdAsync(rotation.ProfileId, cancellationToken);
        if (!IsAllowedIn(profile))
        {
            await _sessions.RevokeAllForProfileAsync(rotation.ProfileId, AdminSessionRevokeReasons.AccountDeactivated, now, cancellationToken);
            return Result.Failure<AdminLoginResponseDto>(ApiErrorCodes.Forbidden, AdminMessages.AccessUnavailable);
        }

        AdminAccessToken access = _adminTokens.GenerateAdminAccessToken(profile!, profile!.Role!.RoleCode, rotation.FamilyId);
        await _audit.LogAsync(AuditAction.TokenRefreshed, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: profile.Id.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
        return Result.Success(ToResponse(profile, access, nextSecret, next.ExpiresAtIst));
    }

    public async Task<Result<string>> LogoutAsync(AdminLogoutRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        bool known = await _sessions.RevokeSessionByTokenAsync(_tokenService.HashToken(request.RefreshToken), AdminSessionRevokeReasons.Logout, Now(), cancellationToken);
        if (known)
        {
            await _audit.LogAsync(AuditAction.Logout, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile),
                ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
        }

        // Same answer whether or not the token was known: signing out is idempotent and reveals nothing.
        return Result.Success(AdminMessages.LoggedOut);
    }

    public async Task<Result<AdminProfileSummaryDto>> GetMeAsync(long adminProfileId, CancellationToken cancellationToken = default)
    {
        AdminProfile? profile = await _profiles.GetByIdAsync(adminProfileId, cancellationToken);
        return IsAllowedIn(profile)
            ? Result.Success(profile!.ToSummary())
            : Result.Failure<AdminProfileSummaryDto>(ApiErrorCodes.Unauthorized, AdminMessages.SessionInvalid);
    }

    // ───────────────────────── internals ─────────────────────────

    private sealed record PasswordCheck(Result? Failure, AdminProfile? Profile);

    private static Result<T> Failed<T>(Result failure) => Result.Failure<T>(failure.ErrorCode!, failure.ErrorMessage!);

    private static bool IsAllowedIn(AdminProfile? profile)
        => profile is { IsActive: true, ActivationStatus: AdminActivationStatus.Active, PasswordHash: not null, Role.IsActive: true };

    /// <summary>
    /// Checks the password of an enabled, activated account. Every wrong-credential outcome (unknown account, no password yet, wrong
    /// password) is the same answer; only a locked or disabled account, which the caller proved the password for, says more.
    /// </summary>
    private async Task<PasswordCheck> CheckPasswordAsync(string nsgNumber, string password, string? ipAddress, CancellationToken cancellationToken)
    {
        string nsg = MobileAuthInput.Nsg(nsgNumber);
        DateTime now = Now();

        AdminProfile? profile = await _profiles.GetByNsgNumberAsync(nsg, cancellationToken);
        if (profile == null)
        {
            _hasher.VerifyPassword(password, DummyHash());
            await AuditLoginFailureAsync(null, "AccountNotMatched", ipAddress, cancellationToken);
            return new PasswordCheck(Result.Failure(ApiErrorCodes.InvalidCredentials, AdminMessages.InvalidCredentials), null);
        }

        if (profile.LockoutEndIst.HasValue)
        {
            if (profile.LockoutEndIst.Value > now)
            {
                await AuditLoginFailureAsync(profile.Id, "Locked", ipAddress, cancellationToken);
                return new PasswordCheck(Result.Failure(ApiErrorCodes.AccountLocked, AdminMessages.AccountLocked), null);
            }
            await _profiles.ClearExpiredLockoutAsync(profile.Id, now, cancellationToken);
        }

        bool canAuthenticate = profile.ActivationStatus == AdminActivationStatus.Active && !string.IsNullOrEmpty(profile.PasswordHash);
        bool passwordOk = false;
        if (canAuthenticate)
            passwordOk = _hasher.VerifyPassword(password, profile.PasswordHash!);
        else
            _hasher.VerifyPassword(password, DummyHash());   // same cost as a real check, result discarded

        if (!passwordOk)
        {
            if (canAuthenticate)
            {
                FailedLoginState state = await _profiles.RegisterFailedLoginAsync(profile.Id, _settings.MaxFailedAttempts, TimeSpan.FromMinutes(_settings.LockoutMinutes), now, cancellationToken);
                await AuditLoginFailureAsync(profile.Id, "WrongPassword", ipAddress, cancellationToken);
                if (state.LockedUntilIst > now)
                    return new PasswordCheck(Result.Failure(ApiErrorCodes.AccountLocked, AdminMessages.AccountLocked), null);
            }
            else
            {
                await AuditLoginFailureAsync(profile.Id, "NotActivated", ipAddress, cancellationToken);
            }
            return new PasswordCheck(Result.Failure(ApiErrorCodes.InvalidCredentials, AdminMessages.InvalidCredentials), null);
        }

        // Correct password, but the account may not be used: disabled accounts and withdrawn roles end any session they still had.
        if (!profile.IsActive || profile.Role is not { IsActive: true })
        {
            await _sessions.RevokeAllForProfileAsync(profile.Id, AdminSessionRevokeReasons.AccountDeactivated, now, cancellationToken);
            await AuditLoginFailureAsync(profile.Id, "AccountDisabled", ipAddress, cancellationToken);
            return new PasswordCheck(Result.Failure(ApiErrorCodes.Forbidden, AdminMessages.AccessUnavailable), null);
        }

        return new PasswordCheck(null, profile);
    }

    /// <summary>The PIS record behind the account when it can still receive the OTP (enabled, with a registered mobile); otherwise null.</summary>
    private async Task<UserPis?> ActivePersonnelAsync(AdminProfile profile, string? ipAddress, CancellationToken cancellationToken)
    {
        UserPis? person = await _pis.GetByNsgNumberAsync(profile.NsgNumber, cancellationToken);
        if (person is { IsActive: true, MobileNumber: not null }) return person;

        await AuditLoginFailureAsync(profile.Id, "PersonnelUnavailable", ipAddress, cancellationToken);
        return null;
    }

    private Task AuditLoginFailureAsync(long? adminProfileId, string reason, string? ipAddress, CancellationToken cancellationToken)
        => _audit.LogAsync(AuditAction.LoginFailure, DomainConstants.AuditResults.Failure, entityType: nameof(AdminProfile), entityId: adminProfileId?.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, additionalData: AdminSupport.Audit(("reason", reason)), cancellationToken: cancellationToken);

    private async Task<AdminLoginResponseDto> StartSessionAsync(AdminProfile profile, string? ipAddress, DateTime now, CancellationToken cancellationToken)
    {
        string refreshSecret = _tokenService.GenerateRefreshToken();
        AdminRefreshToken first = NewRefreshToken(refreshSecret, ipAddress, now);
        first.AdminProfileId = profile.Id;
        first.FamilyId = Guid.NewGuid();

        // One active session per admin: signing in again ends the previous one.
        await _sessions.StartSessionAsync(first, now, cancellationToken);

        AdminAccessToken access = _adminTokens.GenerateAdminAccessToken(profile, profile.Role!.RoleCode, first.FamilyId);
        return ToResponse(profile, access, refreshSecret, first.ExpiresAtIst);
    }

    private AdminRefreshToken NewRefreshToken(string secret, string? ipAddress, DateTime now) => new()
    {
        TokenHash = _tokenService.HashToken(secret),
        CreatedAtIst = now,
        ExpiresAtIst = now.AddDays(_settings.RefreshTokenDays),
        CreatedByIp = ipAddress
    };

    private static AdminLoginResponseDto ToResponse(AdminProfile profile, AdminAccessToken access, string refreshSecret, DateTime refreshExpiresAtIst) => new()
    {
        AccessToken = access.Token,
        ExpiresIn = access.ExpiresInSeconds,
        RefreshToken = refreshSecret,
        RefreshTokenExpiresAt = IstTime.FromIst(refreshExpiresAtIst),
        Profile = profile.ToSummary()
    };
}
