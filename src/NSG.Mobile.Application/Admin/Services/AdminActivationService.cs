using NSG.Mobile.Application.Admin.DTOs;
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
/// First-time activation of a provisioned account: prove control of the PIS-registered mobile with an OTP, then choose a password.
/// Anonymous, so every step re-derives eligibility from the database and answers "not matched" for anything that is not exactly
/// a pending, enabled account whose NSG number, mobile number and date of birth equal the PIS record.
/// </summary>
public interface IAdminActivationService
{
    Task<Result<AdminOtpSentDto>> SendOtpAsync(AdminActivationOtpRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<string>> CompleteAsync(AdminActivationCompleteRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
}

public class AdminActivationService : IAdminActivationService
{
    private readonly IAdminProfileRepository _profiles;
    private readonly IUserPisRepository _pis;
    private readonly IOtpRequestRepository _otpRequests;
    private readonly IOtpService _otpService;
    private readonly IPasswordHashingService _hasher;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly MobileAuthSettings _settings;
    private readonly TimeProvider _time;

    public AdminActivationService(
        IAdminProfileRepository profiles, IUserPisRepository pis, IOtpRequestRepository otpRequests, IOtpService otpService,
        IPasswordHashingService hasher, IAuditService audit, ICurrentUserService currentUser, MobileAuthSettings settings, TimeProvider time)
    {
        _profiles = profiles;
        _pis = pis;
        _otpRequests = otpRequests;
        _otpService = otpService;
        _hasher = hasher;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _time = time;
    }

    private DateTime Now() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<AdminOtpSentDto>> SendOtpAsync(AdminActivationOtpRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        (AdminProfile? profile, UserPis? person) = await FindEligibleAsync(request.NsgNumber, request.MobileNumber, request.DateOfBirth, cancellationToken);
        if (profile == null || person == null)
        {
            await AuditAsync(AuditAction.OtpFailed, DomainConstants.AuditResults.Failure, null, ipAddress, "ActivationNotEligible", cancellationToken);
            return Result.Failure<AdminOtpSentDto>(ApiErrorCodes.MobileNotMatched, AdminMessages.NotMatched);
        }

        // The destination is the number held in PIS (it equals the validated input), never a free client value.
        string destination = AdminSupport.OtpDestination(person);

        int sentLastHour = await _otpRequests.CountSinceAsync(destination, OtpPurpose.AdminActivation, Now().AddHours(-1), cancellationToken);
        if (sentLastHour >= _settings.OtpMaxSendsPerHour)
            return Result.Failure<AdminOtpSentDto>(ApiErrorCodes.OtpRateLimited, AdminMessages.OtpRateLimited);

        OtpGenerationResult sent = await _otpService.GenerateAndSendAsync(destination, OtpPurpose.AdminActivation, cancellationToken);
        if (!sent.Success)
            return Result.Failure<AdminOtpSentDto>(sent.ErrorCode ?? ApiErrorCodes.OtpSendFailed, sent.ErrorMessage ?? "Failed to send OTP. Please try again.");

        await _audit.LogAsync(AuditAction.OtpRequested, DomainConstants.AuditResults.Success, entityType: nameof(OtpRequest), entityId: sent.ReferenceId?.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("flow", "AdminActivation"), ("adminProfileId", profile.Id), ("demo", sent.IsDemo)), cancellationToken: cancellationToken);

        return Result.Success(new AdminOtpSentDto
        {
            OtpReferenceId = sent.ReferenceId!.Value,
            MaskedMobileNumber = PisMasking.Mask(person.MobileNumber!),
            OtpExpiresAt = IstTime.FromIst(sent.ExpiresAtIst ?? Now()),
            DemoOtpMode = sent.IsDemo
        });
    }

    public async Task<Result<string>> CompleteAsync(AdminActivationCompleteRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        (AdminProfile? profile, UserPis? person) = await FindEligibleAsync(request.NsgNumber, request.MobileNumber, request.DateOfBirth, cancellationToken);
        if (profile == null || person == null)
        {
            await AuditAsync(AuditAction.OtpFailed, DomainConstants.AuditResults.Failure, null, ipAddress, "ActivationNotEligible", cancellationToken);
            return Result.Failure<string>(ApiErrorCodes.MobileNotMatched, AdminMessages.NotMatched);
        }

        // The OTP must have been sent to this person's PIS number for activation; anything else looks like an unknown reference.
        OtpRequest? otp = await _otpRequests.GetByReferenceIdAsync(request.OtpReferenceId, cancellationToken);
        if (otp == null || !string.Equals(otp.MobileNumber, AdminSupport.OtpDestination(person), StringComparison.Ordinal))
            return Result.Failure<string>(ApiErrorCodes.OtpInvalid, AdminMessages.OtpReferenceInvalid);

        OtpVerificationResult verified = await _otpService.VerifyAsync(request.OtpReferenceId, request.OtpCode, OtpPurpose.AdminActivation, cancellationToken);
        if (!verified.Success)
        {
            await _audit.LogAsync(AuditAction.OtpFailed, DomainConstants.AuditResults.Failure, entityType: nameof(OtpRequest), entityId: request.OtpReferenceId.ToString(),
                ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
                additionalData: AdminSupport.Audit(("flow", "AdminActivation"), ("errorCode", verified.ErrorCode)), cancellationToken: cancellationToken);
            return Result.Failure<string>(verified.ErrorCode ?? ApiErrorCodes.OtpInvalid, verified.ErrorMessage ?? "OTP verification failed.");
        }

        await _audit.LogAsync(AuditAction.OtpVerified, DomainConstants.AuditResults.Success, entityType: nameof(OtpRequest), entityId: request.OtpReferenceId.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, additionalData: AdminSupport.Audit(("flow", "AdminActivation")), cancellationToken: cancellationToken);

        // Only the hash is stored. The conditional update makes activation happen exactly once.
        bool activated = await _profiles.ActivateAsync(profile.Id, _hasher.HashPassword(request.NewPassword), Now(), cancellationToken);
        if (!activated)
            return Result.Failure<string>(ApiErrorCodes.MobileNotMatched, AdminMessages.NotMatched);

        await AuditAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, profile.Id, ipAddress, "AdminAccountActivated", cancellationToken);
        return Result.Success(AdminMessages.AccountActivated);
    }

    /// <summary>The pending, enabled account and the PIS record that matches the supplied identifiers, or nulls.</summary>
    private async Task<(AdminProfile? Profile, UserPis? Person)> FindEligibleAsync(string nsgNumber, string mobile, DateOnly dateOfBirth, CancellationToken cancellationToken)
    {
        string nsg = MobileAuthInput.Nsg(nsgNumber);
        AdminProfile? profile = await _profiles.GetByNsgNumberAsync(nsg, cancellationToken);
        UserPis? person = await AdminSupport.FindMatchingPersonnelAsync(_pis, nsg, MobileAuthInput.Mobile(mobile), dateOfBirth, cancellationToken);

        bool eligible = profile is { IsActive: true, ActivationStatus: AdminActivationStatus.PendingActivation, PasswordHash: null }
            && profile.Role is { IsActive: true }
            && person != null;
        return eligible ? (profile, person) : (null, null);
    }

    private Task AuditAsync(AuditAction action, string result, long? adminProfileId, string? ipAddress, string detail, CancellationToken cancellationToken)
        => _audit.LogAsync(action, result, entityType: nameof(AdminProfile), entityId: adminProfileId?.ToString(), ipAddress: ipAddress,
            correlationId: _currentUser.CorrelationId, additionalData: AdminSupport.Audit(("detail", detail)), cancellationToken: cancellationToken);
}
