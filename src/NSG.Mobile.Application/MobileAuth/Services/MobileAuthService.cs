using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.MobileAuth.Services;

/// <summary>
/// Mobile registration, OTP, MPIN and login workflow (ADR-014). Every endpoint re-derives eligibility from the database;
/// nothing the client says about role, approval status or identity is trusted. Stateless between calls: the only things that
/// carry progress are the OTP record and the short-lived, single-use <see cref="MobileAuthGrant"/>.
/// </summary>
public class MobileAuthService : IMobileAuthService
{
    private const string IndiaPrefix = DomainConstants.MobileNumber.IndiaCountryCode;

    // Verified against when no profile matches, so "no such profile" and "wrong MPIN" cost the same time.
    private static string? s_dummyHash;

    private readonly IMobileUserProfileRepository _profiles;
    private readonly IApproveMobileUserRepository _requests;
    private readonly IMobileAuthGrantRepository _grants;
    private readonly IMobileRefreshTokenRepository _sessions;
    private readonly IUserPisRepository _pis;
    private readonly IOtpRequestRepository _otpRequests;
    private readonly IOtpService _otpService;
    private readonly IPasswordHashingService _hasher;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly MobileAuthSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<MobileAuthService> _logger;

    public MobileAuthService(
        IMobileUserProfileRepository profiles,
        IApproveMobileUserRepository requests,
        IMobileAuthGrantRepository grants,
        IMobileRefreshTokenRepository sessions,
        IUserPisRepository pis,
        IOtpRequestRepository otpRequests,
        IOtpService otpService,
        IPasswordHashingService hasher,
        ITokenService tokenService,
        IAuditService audit,
        ICurrentUserService currentUser,
        MobileAuthSettings settings,
        TimeProvider time,
        ILogger<MobileAuthService> logger)
    {
        _profiles = profiles;
        _requests = requests;
        _grants = grants;
        _sessions = sessions;
        _pis = pis;
        _otpRequests = otpRequests;
        _otpService = otpService;
        _hasher = hasher;
        _tokenService = tokenService;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    // ───────────────────────── 1. login/check ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> CheckAsync(MobileLoginCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        Identity id = Identity.From(request);
        Eligibility eligibility = await EvaluateAsync(id, cancellationToken);

        // Dates come from approve_mobile_users: the pending request's requested_at_ist, or for an approved person the approval
        // (requested_at_ist and approved_at_ist of the latest approved request). A field with no row behind it is omitted, never null.
        switch (eligibility.Kind)
        {
            case EligibilityKind.PendingRequest:
                return WithDates(Terminal(eligibility), eligibility.Request);

            case EligibilityKind.ActiveProfile or EligibilityKind.OtpEligible:
                ApproveMobileUser? approved = await _requests.GetLatestApprovedByNsgNumberAsync(id.Nsg, cancellationToken);
                return WithDates(
                    Flow(eligibility.Kind == EligibilityKind.ActiveProfile ? MobileAuthStatus.MpinRequired : MobileAuthStatus.OtpRequired),
                    approved);

            default:
                return Terminal(eligibility);
        }
    }

    private static Result<MobileAuthFlowDto> WithDates(Result<MobileAuthFlowDto> result, ApproveMobileUser? request)
    {
        if (result.IsSuccess && request != null)
        {
            result.Data!.RequestedAt = IstTime.FromIst(request.RequestedAtIst);
            if (request.ApprovedAtIst.HasValue)
                result.Data.ApprovedAt = IstTime.FromIst(request.ApprovedAtIst.Value);
        }

        return result;
    }

    // ───────────────────────── 2. login ─────────────────────────

    public async Task<Result<MobileLoginResponseDto>> LoginAsync(MobileLoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Identity id = Identity.From(request);
        DateTime now = Now();

        MobileUserProfile? profile = await FindMatchingProfileAsync(id, cancellationToken);
        if (profile == null)
        {
            _hasher.VerifyPassword(request.Mpin, DummyHash());
            await _audit.LogAsync(AuditAction.LoginFailure, DomainConstants.AuditResults.Failure, ipAddress: ipAddress,
                correlationId: _currentUser.CorrelationId, additionalData: "{\"reason\":\"ProfileNotMatched\"}", cancellationToken: cancellationToken);
            return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.InvalidCredentials, MobileAuthMessages.InvalidCredentials);
        }

        if (!profile.IsActive)
        {
            await _sessions.RevokeAllForProfileAsync(profile.Id, MobileSessionRevokeReasons.ProfileInactive, now, cancellationToken);
            await AuditLoginFailureAsync(profile, "ProfileInactive", ipAddress, cancellationToken);
            return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.Forbidden, MobileAuthMessages.AccessUnavailable);
        }

        if (profile.LockedUntilIst.HasValue)
        {
            if (profile.LockedUntilIst.Value > now)
            {
                await AuditLoginFailureAsync(profile, "Locked", ipAddress, cancellationToken);
                return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.AccountLocked, MobileAuthMessages.AccountLocked);
            }

            await _profiles.ClearExpiredLockoutAsync(profile.Id, now, cancellationToken);
        }

        if (string.IsNullOrEmpty(profile.MpinHash) || !_hasher.VerifyPassword(request.Mpin, profile.MpinHash))
        {
            Result<MobileLoginResponseDto>? failure = await RegisterFailureAsync<MobileLoginResponseDto>(profile, ipAddress, cancellationToken);
            return failure;
        }

        await _profiles.RecordSuccessfulLoginAsync(profile.Id, now, cancellationToken);

        // Tokens are issued only here, after the MPIN has been verified against an active, unlocked profile.
        MobileLoginResponseDto session = await StartSessionAsync(profile, ipAddress, now, cancellationToken);

        await _audit.LogAsync(AuditAction.LoginSuccess, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
            entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        return Result.Success(session);
    }

    // ───────────────────────── sessions: refresh and logout ─────────────────────────

    public async Task<Result<MobileLoginResponseDto>> RefreshAsync(MobileRefreshRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        DateTime now = Now();
        string presentedHash = _tokenService.HashToken(request.RefreshToken);

        string nextSecret = _tokenService.GenerateRefreshToken();
        MobileRefreshToken next = NewRefreshToken(nextSecret, ipAddress, now);

        RefreshRotationResult rotation = await _sessions.RotateAsync(presentedHash, next, now, cancellationToken);
        switch (rotation.Outcome)
        {
            case RefreshRotationOutcome.ReuseDetected:
                await _audit.LogAsync(AuditAction.TokenReuseDetected, DomainConstants.AuditResults.Failure, entityType: nameof(MobileUserProfile),
                    entityId: rotation.ProfileId.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
                return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.RefreshTokenReuseDetected, MobileAuthMessages.SessionEnded);

            case RefreshRotationOutcome.Expired:
                return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.RefreshTokenExpired, MobileAuthMessages.SessionExpired);

            case RefreshRotationOutcome.Invalid:
                return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.RefreshTokenInvalid, MobileAuthMessages.SessionInvalid);
        }

        // The profile must still be allowed in; a deactivated profile loses every session at its next refresh.
        MobileUserProfile? profile = await _profiles.GetByIdAsync(rotation.ProfileId, cancellationToken);
        if (profile == null || !profile.IsActive)
        {
            await _sessions.RevokeAllForProfileAsync(rotation.ProfileId, MobileSessionRevokeReasons.ProfileInactive, now, cancellationToken);
            return Result.Failure<MobileLoginResponseDto>(ApiErrorCodes.Forbidden, MobileAuthMessages.AccessUnavailable);
        }

        MobileAccessToken access = _tokenService.GenerateMobileAccessToken(profile, rotation.FamilyId);
        await _audit.LogAsync(AuditAction.TokenRefreshed, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
            entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        return Result.Success(ToResponse(profile, access, nextSecret, next.ExpiresAtIst));
    }

    public async Task<Result<MobileAuthFlowDto>> LogoutAsync(MobileLogoutRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        bool known = await _sessions.RevokeSessionByTokenAsync(_tokenService.HashToken(request.RefreshToken), MobileSessionRevokeReasons.Logout, Now(), cancellationToken);
        if (known)
        {
            await _audit.LogAsync(AuditAction.Logout, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
                ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
        }

        // Same answer whether or not the token was known: logging out is idempotent and reveals nothing.
        return Result.Success(new MobileAuthFlowDto { Status = MobileAuthStatus.LoggedOut });
    }

    private async Task<MobileLoginResponseDto> StartSessionAsync(MobileUserProfile profile, string? ipAddress, DateTime now, CancellationToken cancellationToken)
    {
        string refreshSecret = _tokenService.GenerateRefreshToken();
        MobileRefreshToken first = NewRefreshToken(refreshSecret, ipAddress, now);
        first.ProfileId = profile.Id;
        first.FamilyId = Guid.NewGuid();

        // One active session per profile: signing in again ends the previous one.
        await _sessions.StartSessionAsync(first, now, cancellationToken);

        MobileAccessToken access = _tokenService.GenerateMobileAccessToken(profile, first.FamilyId);
        return ToResponse(profile, access, refreshSecret, first.ExpiresAtIst);
    }

    private MobileRefreshToken NewRefreshToken(string secret, string? ipAddress, DateTime now) => new()
    {
        TokenHash = _tokenService.HashToken(secret),
        CreatedAtIst = now,
        ExpiresAtIst = now.AddDays(_settings.RefreshTokenDays),
        CreatedByIp = ipAddress
    };

    private static MobileLoginResponseDto ToResponse(MobileUserProfile profile, MobileAccessToken access, string refreshSecret, DateTime refreshExpiresAtIst) => new()
    {
        AccessToken = access.Token,
        ExpiresIn = access.ExpiresInSeconds,
        RefreshToken = refreshSecret,
        RefreshTokenExpiresAt = IstTime.FromIst(refreshExpiresAtIst),
        Profile = new MobileProfileSummaryDto { Id = profile.Id, NsgNumber = profile.NsgNumber, Role = profile.Role.ToString() }
    };

    // ───────────────────────── 3. otp/send ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> SendOtpAsync(MobileOtpSendRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Identity id = Identity.From(request);
        Eligibility eligibility = await EvaluateAsync(id, cancellationToken);

        Result<MobileAuthFlowDto>? blocked = GateForFlow(request.Flow, eligibility);
        if (blocked != null) return blocked;

        // The destination is the number held in PIS / on the profile (it equals the validated input), never a free client value.
        string destination = IndiaPrefix + eligibility.RegisteredMobile!;
        OtpPurpose purpose = PurposeFor(request.Flow);

        int sentLastHour = await _otpRequests.CountSinceAsync(destination, purpose, Now().AddHours(-1), cancellationToken);
        if (sentLastHour >= _settings.OtpMaxSendsPerHour)
        {
            return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.OtpRateLimited, "Too many OTP requests. Please try again later.");
        }

        OtpGenerationResult sent = await _otpService.GenerateAndSendAsync(destination, purpose, cancellationToken);
        if (!sent.Success)
        {
            return Result.Failure<MobileAuthFlowDto>(sent.ErrorCode ?? ApiErrorCodes.OtpSendFailed, sent.ErrorMessage ?? "Failed to send OTP. Please try again.");
        }

        await _audit.LogAsync(AuditAction.OtpRequested, DomainConstants.AuditResults.Success, entityType: nameof(OtpRequest),
            entityId: sent.ReferenceId?.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"flow\":\"{request.Flow}\",\"demo\":{(sent.IsDemo ? "true" : "false")}}}", cancellationToken: cancellationToken);

        DateTime expiresAt = sent.ExpiresAtIst ?? Now();
        MobileAuthFlowDto dto = new()
        {
            Status = MobileAuthStatus.OtpSent,
            OtpReferenceId = sent.ReferenceId,
            MaskedMobileNumber = PisMasking.Mask(eligibility.RegisteredMobile!),
            OtpExpiresAt = IstTime.FromIst(expiresAt),
            DemoOtpMode = sent.IsDemo
        };
        if (sent.IsDemo) dto.Message = MobileAuthMessages.DemoOtpSent;   // never claim an SMS went out
        return Result.Success(dto);
    }

    // ───────────────────────── 4. otp/verify ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> VerifyOtpAsync(MobileOtpVerifyRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Identity id = Identity.From(request);
        Eligibility before = await EvaluateAsync(id, cancellationToken);

        Result<MobileAuthFlowDto>? blocked = GateForFlow(request.Flow, before);
        if (blocked != null) return blocked;

        // The OTP must have been sent to this person's registered number for this flow; anything else looks like an unknown reference.
        OtpRequest? otp = await _otpRequests.GetByReferenceIdAsync(request.OtpReferenceId, cancellationToken);
        if (otp == null || !string.Equals(otp.MobileNumber, IndiaPrefix + before.RegisteredMobile, StringComparison.Ordinal))
        {
            return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.OtpInvalid, "Invalid OTP reference.");
        }

        OtpVerificationResult verified = await _otpService.VerifyAsync(request.OtpReferenceId, request.OtpCode, PurposeFor(request.Flow), cancellationToken);
        if (!verified.Success)
        {
            await _audit.LogAsync(AuditAction.OtpFailed, DomainConstants.AuditResults.Failure, entityType: nameof(OtpRequest),
                entityId: request.OtpReferenceId.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"errorCode\":\"{verified.ErrorCode}\"}}", cancellationToken: cancellationToken);
            return Result.Failure<MobileAuthFlowDto>(verified.ErrorCode ?? ApiErrorCodes.OtpInvalid, verified.ErrorMessage ?? "OTP verification failed.");
        }

        await _audit.LogAsync(AuditAction.OtpVerified, DomainConstants.AuditResults.Success, entityType: nameof(OtpRequest),
            entityId: request.OtpReferenceId.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        // Revalidate: the world may have changed while the person was reading the SMS.
        Eligibility after = await EvaluateAsync(id, cancellationToken);

        if (request.Flow == MobileOtpFlow.MpinReset)
        {
            return after.Kind == EligibilityKind.ActiveProfile
                ? Result.Success(await IssueGrantAsync(MobileGrantPurpose.MpinReset, after.Profile!.NsgNumber, after.Profile.MobileNumber, MobileAuthStatus.MpinResetGranted, cancellationToken))
                : GateForFlow(request.Flow, after) ?? Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.MobileNotMatched, MobileAuthMessages.NotMatched);
        }

        if (after.Kind != EligibilityKind.OtpEligible)
        {
            return after.Kind == EligibilityKind.ActiveProfile ? Flow(MobileAuthStatus.MpinRequired) : Terminal(after);
        }

        UserPis pis = after.Pis!;
        switch (pis.MobileAppActivationStatus)
        {
            case MobileAppActivationStatus.Pending:
                return await RaiseApprovalRequestAsync(pis, ipAddress, cancellationToken);

            case MobileAppActivationStatus.Approved:
                // Already approved: no approval request, just proof that this person controls the registered number.
                return Result.Success(await IssueGrantAsync(MobileGrantPurpose.RegistrationVerified, pis.NsgNumber, pis.MobileNumber!, MobileAuthStatus.RegistrationVerified, cancellationToken));

            default:
                return Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable);
        }
    }

    // ───────────────────────── 5. registration/submit ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> SubmitRegistrationAsync(RegistrationSubmitRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        MobileAuthGrant? grant = await FindGrantAsync(request.GrantToken, MobileGrantPurpose.RegistrationVerified, cancellationToken);
        if (grant == null) return GrantInvalid<MobileAuthFlowDto>();

        UserPis? pis = await _pis.GetByNsgNumberAsync(grant.NsgNumber, cancellationToken);
        if (pis == null || !string.Equals(pis.MobileNumber, grant.MobileNumber, StringComparison.Ordinal))
        {
            return GrantInvalid<MobileAuthFlowDto>();
        }

        // A profile may have been created since the OTP: nothing to register.
        MobileUserProfile? existing = await _profiles.GetByNsgNumberAsync(grant.NsgNumber, cancellationToken)
                                      ?? await _profiles.GetByMobileNumberAsync(grant.MobileNumber, cancellationToken);
        if (existing != null)
        {
            if (!await _grants.TryConsumeAsync(grant.Id, Now(), cancellationToken)) return GrantInvalid<MobileAuthFlowDto>();
            bool sameOwner = string.Equals(existing.NsgNumber, grant.NsgNumber, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(existing.MobileNumber, grant.MobileNumber, StringComparison.Ordinal);
            if (!sameOwner) return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.Conflict, "This registration cannot be completed. Please contact the IT Admin.");
            return existing.IsActive ? Flow(MobileAuthStatus.MpinRequired) : Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable);
        }

        if (!pis.IsActive || pis.MobileAppActivationStatus == MobileAppActivationStatus.Rejected)
        {
            await _grants.TryConsumeAsync(grant.Id, Now(), cancellationToken);
            return Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable);
        }

        if (await _requests.GetPendingByNsgNumberAsync(grant.NsgNumber, cancellationToken) != null)
        {
            await _grants.TryConsumeAsync(grant.Id, Now(), cancellationToken);
            return Flow(MobileAuthStatus.RequestPending, MobileAuthMessages.RequestAlreadyPending);
        }

        if (pis.MobileAppActivationStatus == MobileAppActivationStatus.Pending)
        {
            // Status went back to Pending after OTP: the OTP in this grant is the proof an approval request needs. No profile is created.
            if (!await _grants.TryConsumeAsync(grant.Id, Now(), cancellationToken)) return GrantInvalid<MobileAuthFlowDto>();
            return await RaiseApprovalRequestAsync(pis, ipAddress, cancellationToken);
        }

        // Approved: no approval request; hand over the grant that allows the MPIN to be set.
        if (!await _grants.TryConsumeAsync(grant.Id, Now(), cancellationToken)) return GrantInvalid<MobileAuthFlowDto>();

        await _audit.LogAsync(AuditAction.RegistrationSubmitted, DomainConstants.AuditResults.Success, ipAddress: ipAddress,
            correlationId: _currentUser.CorrelationId, additionalData: "{\"path\":\"Approved\"}", cancellationToken: cancellationToken);

        return Result.Success(await IssueGrantAsync(MobileGrantPurpose.MpinSetup, pis.NsgNumber, pis.MobileNumber!, MobileAuthStatus.MpinSetupGranted, cancellationToken));
    }

    // ───────────────────────── 6. mpin/setup ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> SetupMpinAsync(MpinSetupRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        MobileAuthGrant? grant = await FindGrantAsync(request.GrantToken, MobileGrantPurpose.MpinSetup, cancellationToken);
        if (grant == null) return GrantInvalid<MobileAuthFlowDto>();

        // Recheck everything the grant stands for; the profile's data comes from PIS, never from the request.
        UserPis? pis = await _pis.GetByNsgNumberAsync(grant.NsgNumber, cancellationToken);
        if (pis == null || !pis.IsActive || pis.MobileAppActivationStatus != MobileAppActivationStatus.Approved
            || pis.DateOfBirth == null || !string.Equals(pis.MobileNumber, grant.MobileNumber, StringComparison.Ordinal))
        {
            return Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable);
        }

        if (await _profiles.GetByNsgNumberAsync(pis.NsgNumber, cancellationToken) != null
            || await _profiles.GetByMobileNumberAsync(grant.MobileNumber, cancellationToken) != null)
        {
            return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.Conflict, "An account already exists for these details.");
        }

        DateTime now = Now();
        MobileUserProfile profile = new()
        {
            NsgNumber = pis.NsgNumber,
            MobileNumber = pis.MobileNumber!,
            DateOfBirth = pis.DateOfBirth.Value,
            Role = UserRole.Employee,
            MpinHash = _hasher.HashPassword(request.Mpin),
            IsActive = true,
            CreatedAtIst = now
        };

        ProfileCreateOutcome outcome = await _profiles.CreateWithGrantAsync(profile, grant.Id, now, cancellationToken);
        switch (outcome)
        {
            case ProfileCreateOutcome.Created:
                await _audit.LogAsync(AuditAction.MobileProfileCreated, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
                    entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
                return Result.Success(new MobileAuthFlowDto { Status = MobileAuthStatus.ProfileCreated });

            case ProfileCreateOutcome.GrantUnavailable:
                return GrantInvalid<MobileAuthFlowDto>();

            default:
                return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.Conflict, "An account already exists for these details.");
        }
    }

    // ───────────────────────── 7. mpin/change ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> ChangeMpinAsync(MpinChangeRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null) return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.Unauthorized, "Authentication is required.");

        MobileUserProfile? profile = await _profiles.GetByIdAsync(profileId.Value, cancellationToken);
        if (profile == null || !profile.IsActive)
        {
            return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.Forbidden, MobileAuthMessages.AccessUnavailable);
        }

        DateTime now = Now();
        if (profile.LockedUntilIst.HasValue)
        {
            if (profile.LockedUntilIst.Value > now)
                return Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.AccountLocked, MobileAuthMessages.AccountLocked);
            await _profiles.ClearExpiredLockoutAsync(profile.Id, now, cancellationToken);
        }

        if (string.IsNullOrEmpty(profile.MpinHash) || !_hasher.VerifyPassword(request.CurrentMpin, profile.MpinHash))
        {
            // A wrong current MPIN counts like a wrong login: an attacker holding a stolen token must not get free guesses.
            return await RegisterFailureAsync<MobileAuthFlowDto>(profile, ipAddress, cancellationToken, "Current MPIN is incorrect.");
        }

        await _profiles.SetMpinAsync(profile.Id, _hasher.HashPassword(request.NewMpin), now, cancellationToken);

        await _audit.LogAsync(AuditAction.MpinChanged, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
            entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        return Result.Success(new MobileAuthFlowDto { Status = MobileAuthStatus.MpinChanged });
    }

    // ───────────────────────── 8. mpin/reset ─────────────────────────

    public async Task<Result<MobileAuthFlowDto>> ResetMpinAsync(MpinResetRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        MobileAuthGrant? grant = await FindGrantAsync(request.GrantToken, MobileGrantPurpose.MpinReset, cancellationToken);
        if (grant == null) return GrantInvalid<MobileAuthFlowDto>();

        MobileUserProfile? profile = await _profiles.GetByNsgNumberAsync(grant.NsgNumber, cancellationToken);
        if (profile == null || !string.Equals(profile.MobileNumber, grant.MobileNumber, StringComparison.Ordinal))
        {
            return GrantInvalid<MobileAuthFlowDto>();
        }

        if (!profile.IsActive) return Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable);

        DateTime now = Now();
        bool reset = await _profiles.ResetMpinWithGrantAsync(profile.Id, profile.NsgNumber, _hasher.HashPassword(request.NewMpin), grant.Id, now, cancellationToken);
        if (!reset) return GrantInvalid<MobileAuthFlowDto>();

        await _audit.LogAsync(AuditAction.MpinReset, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
            entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        return Result.Success(new MobileAuthFlowDto { Status = MobileAuthStatus.MpinReset });
    }

    // ───────────────────────── eligibility ─────────────────────────

    private enum EligibilityKind { NotMatched, ActiveProfile, InactiveProfile, PendingRequest, AccessUnavailable, OtpEligible }

    private sealed record Eligibility(EligibilityKind Kind, UserPis? Pis = null, MobileUserProfile? Profile = null, ApproveMobileUser? Request = null)
    {
        /// <summary>The mobile number on record (ten digits) for the matched person.</summary>
        public string? RegisteredMobile => Profile?.MobileNumber ?? Pis?.MobileNumber;
    }

    private sealed record Identity(string Nsg, string Mobile, DateOnly DateOfBirth)
    {
        public static Identity From(MobileIdentityRequestDto dto)
            => new(MobileAuthInput.Nsg(dto.NsgNumber), MobileAuthInput.Mobile(dto.MobileNumber), dto.DateOfBirth);
    }

    private static bool Matches(Identity id, string nsg, string mobile, DateOnly dob)
        => string.Equals(nsg, id.Nsg, StringComparison.OrdinalIgnoreCase)
           && string.Equals(mobile, id.Mobile, StringComparison.Ordinal)
           && dob == id.DateOfBirth;

    /// <summary>The profile that matches all three identifiers, or null. A profile that matches only some of them is treated as no match.</summary>
    private async Task<MobileUserProfile?> FindMatchingProfileAsync(Identity id, CancellationToken cancellationToken)
    {
        MobileUserProfile? byNsg = await _profiles.GetByNsgNumberAsync(id.Nsg, cancellationToken);
        if (byNsg != null && Matches(id, byNsg.NsgNumber, byNsg.MobileNumber, byNsg.DateOfBirth)) return byNsg;

        MobileUserProfile? byMobile = await _profiles.GetByMobileNumberAsync(id.Mobile, cancellationToken);
        return byMobile != null && Matches(id, byMobile.NsgNumber, byMobile.MobileNumber, byMobile.DateOfBirth) ? byMobile : null;
    }

    /// <summary>
    /// The decision order of the business rules: profile first, then a pending request (without touching PIS), then PIS.
    /// </summary>
    private async Task<Eligibility> EvaluateAsync(Identity id, CancellationToken cancellationToken)
    {
        // 1. Mobile profile first. Any profile on this NSG number or mobile number settles the question; if it is not
        //    this person's (not all three identifiers match) the answer is a generic "no match", never a hint about whose it is.
        MobileUserProfile? byNsg = await _profiles.GetByNsgNumberAsync(id.Nsg, cancellationToken);
        MobileUserProfile? byMobile = await _profiles.GetByMobileNumberAsync(id.Mobile, cancellationToken);
        if (byNsg != null || byMobile != null)
        {
            MobileUserProfile? own = byNsg != null && Matches(id, byNsg.NsgNumber, byNsg.MobileNumber, byNsg.DateOfBirth) ? byNsg
                : byMobile != null && Matches(id, byMobile.NsgNumber, byMobile.MobileNumber, byMobile.DateOfBirth) ? byMobile
                : null;
            if (own == null) return new Eligibility(EligibilityKind.NotMatched);
            return new Eligibility(own.IsActive ? EligibilityKind.ActiveProfile : EligibilityKind.InactiveProfile, Profile: own);
        }

        // 2. A pending request for this NSG number ends the flow here: PIS is not queried, no OTP is sent, no request is created.
        //    It is only acknowledged to someone who also knows the mobile number and date of birth on the request.
        ApproveMobileUser? pending = await _requests.GetPendingByNsgNumberAsync(id.Nsg, cancellationToken);
        if (pending != null)
        {
            return Matches(id, pending.NsgNumber, pending.MobileNumber, pending.DateOfBirth)
                ? new Eligibility(EligibilityKind.PendingRequest, Request: pending)
                : new Eligibility(EligibilityKind.NotMatched);
        }

        // 3. Match all three identifiers against PIS.
        UserPis? pis = await _pis.GetByNsgNumberAsync(id.Nsg, cancellationToken);
        if (pis == null || pis.DateOfBirth == null || pis.MobileNumber == null || !Matches(id, pis.NsgNumber, pis.MobileNumber, pis.DateOfBirth.Value))
        {
            return new Eligibility(EligibilityKind.NotMatched);
        }

        if (!pis.IsActive || pis.MobileAppActivationStatus == MobileAppActivationStatus.Rejected)
        {
            return new Eligibility(EligibilityKind.AccessUnavailable, pis);
        }

        return new Eligibility(EligibilityKind.OtpEligible, pis);
    }

    /// <summary>The response for every eligibility that ends the flow without an OTP (everything except OtpEligible / ActiveProfile).</summary>
    private static Result<MobileAuthFlowDto> Terminal(Eligibility eligibility) => eligibility.Kind switch
    {
        EligibilityKind.InactiveProfile or EligibilityKind.AccessUnavailable => Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable),
        EligibilityKind.PendingRequest => Flow(MobileAuthStatus.RequestPending, MobileAuthMessages.RequestAlreadyPending),
        _ => Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.MobileNotMatched, MobileAuthMessages.NotMatched)
    };

    /// <summary>Null when the person may proceed with an OTP in this flow; otherwise the response that ends the flow.</summary>
    private static Result<MobileAuthFlowDto>? GateForFlow(MobileOtpFlow flow, Eligibility eligibility)
    {
        if (flow == MobileOtpFlow.Registration)
        {
            return eligibility.Kind switch
            {
                EligibilityKind.OtpEligible => null,
                EligibilityKind.ActiveProfile => Flow(MobileAuthStatus.MpinRequired),
                _ => Terminal(eligibility)
            };
        }

        // MPIN recovery is for an existing, active profile only; every other situation is a generic "no match" (or the IT-support notice).
        return eligibility.Kind switch
        {
            EligibilityKind.ActiveProfile => null,
            EligibilityKind.InactiveProfile => Flow(MobileAuthStatus.AccessUnavailable, MobileAuthMessages.AccessUnavailable),
            _ => Result.Failure<MobileAuthFlowDto>(ApiErrorCodes.MobileNotMatched, MobileAuthMessages.NotMatched)
        };
    }

    private static OtpPurpose PurposeFor(MobileOtpFlow flow) => flow == MobileOtpFlow.MpinReset ? OtpPurpose.PasswordReset : OtpPurpose.Registration;

    // ───────────────────────── approval request ─────────────────────────

    private async Task<Result<MobileAuthFlowDto>> RaiseApprovalRequestAsync(UserPis pis, string? ipAddress, CancellationToken cancellationToken)
    {
        DateTime now = Now();
        ApproveMobileUser request = new()
        {
            NsgNumber = pis.NsgNumber,
            MobileNumber = pis.MobileNumber!,
            DateOfBirth = pis.DateOfBirth!.Value,
            ApprovalStatus = MobileAppActivationStatus.Pending,
            RequestedAtIst = now,
            CreatedAtIst = now
        };

        PendingRequestOutcome outcome = await _requests.AddPendingAsync(request, cancellationToken);
        if (outcome == PendingRequestOutcome.AlreadyPending)
        {
            return Flow(MobileAuthStatus.RequestPending, MobileAuthMessages.RequestAlreadyPending);
        }

        await _audit.LogAsync(AuditAction.MobileApprovalRequested, DomainConstants.AuditResults.Success, entityType: nameof(ApproveMobileUser),
            entityId: request.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);

        return Flow(MobileAuthStatus.ApprovalRequested, MobileAuthMessages.ApprovalRaised);
    }

    // ───────────────────────── failed MPIN ─────────────────────────

    private async Task<Result<T>> RegisterFailureAsync<T>(MobileUserProfile profile, string? ipAddress, CancellationToken cancellationToken, string? invalidMessage = null)
    {
        DateTime now = Now();
        FailedLoginState state = await _profiles.RegisterFailedLoginAsync(profile.Id, _settings.MaxFailedAttempts,
            TimeSpan.FromMinutes(_settings.LockoutMinutes), now, cancellationToken);

        await AuditLoginFailureAsync(profile, "InvalidMpin", ipAddress, cancellationToken);

        if (state.LockedUntilIst.HasValue && state.LockedUntilIst.Value > now)
        {
            await _audit.LogAsync(AuditAction.AccountLocked, DomainConstants.AuditResults.Success, entityType: nameof(MobileUserProfile),
                entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, cancellationToken: cancellationToken);
            return Result.Failure<T>(ApiErrorCodes.AccountLocked, MobileAuthMessages.AccountLocked);
        }

        return Result.Failure<T>(ApiErrorCodes.InvalidCredentials, invalidMessage ?? MobileAuthMessages.InvalidCredentials);
    }

    private Task AuditLoginFailureAsync(MobileUserProfile profile, string reason, string? ipAddress, CancellationToken cancellationToken)
        => _audit.LogAsync(AuditAction.LoginFailure, DomainConstants.AuditResults.Failure, entityType: nameof(MobileUserProfile),
            entityId: profile.Id.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"reason\":\"{reason}\"}}", cancellationToken: cancellationToken);

    // ───────────────────────── grants ─────────────────────────

    private async Task<MobileAuthFlowDto> IssueGrantAsync(MobileGrantPurpose purpose, string nsgNumber, string mobileNumber, string status, CancellationToken cancellationToken)
    {
        DateTime now = Now();

        // Only the newest grant of a person is valid: issuing one retires the rest.
        await _grants.InvalidateForNsgNumberAsync(nsgNumber, now, cancellationToken);

        string secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        MobileAuthGrant grant = new()
        {
            TokenHash = _tokenService.HashToken(secret),
            Purpose = purpose,
            NsgNumber = nsgNumber,
            MobileNumber = mobileNumber,
            CreatedAtIst = now,
            ExpiresAtIst = now.AddMinutes(_settings.GrantLifetimeMinutes)
        };
        await _grants.AddAsync(grant, cancellationToken);

        return new MobileAuthFlowDto { Status = status, GrantToken = secret, GrantExpiresAt = IstTime.FromIst(grant.ExpiresAtIst) };
    }

    /// <summary>The grant for this secret and purpose if it is still usable; null otherwise (unknown, wrong purpose, used or expired).</summary>
    private async Task<MobileAuthGrant?> FindGrantAsync(string secret, MobileGrantPurpose purpose, CancellationToken cancellationToken)
    {
        MobileAuthGrant? grant = await _grants.GetByTokenHashAsync(_tokenService.HashToken(secret), cancellationToken);
        return grant != null && grant.Purpose == purpose && grant.ConsumedAtIst == null && grant.ExpiresAtIst > Now() ? grant : null;
    }

    private static Result<T> GrantInvalid<T>() => Result.Failure<T>(ApiErrorCodes.GrantInvalid, MobileAuthMessages.GrantInvalid);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ───────────────────────── helpers ─────────────────────────

    private DateTime Now() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    private static Result<MobileAuthFlowDto> Flow(string status, string? message = null)
    {
        MobileAuthFlowDto dto = new() { Status = status };
        if (message != null) dto.Message = message;
        return Result.Success(dto);
    }

    private string DummyHash() => s_dummyHash ??= _hasher.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
}
