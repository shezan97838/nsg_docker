using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// OTP generation, hashing, verification, and SMS dispatch.
/// Enforces attempt limits, expiration, cooldown, and purpose isolation.
/// OTP values are hashed before storage — never stored in plaintext.
/// </summary>
public class OtpService : IOtpService
{
    private readonly IOtpRequestRepository _otpRepository;
    private readonly INicSmsService _smsService;
    private readonly SecurityOptions _securityOptions;
    private readonly OtpOptions _otpOptions;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        IOtpRequestRepository otpRepository,
        INicSmsService smsService,
        IOptions<SecurityOptions> securityOptions,
        IOptions<OtpOptions> otpOptions,
        IHostEnvironment environment,
        ILogger<OtpService> logger)
    {
        _otpRepository = otpRepository;
        _smsService = smsService;
        _securityOptions = securityOptions.Value;
        _otpOptions = otpOptions.Value;

        // Second line of defence behind the startup check in Program.cs: a demo-mode service cannot even be constructed in Production.
        OtpModeGuard.EnsureAllowed(_otpOptions, environment.EnvironmentName);
        _logger = logger;
    }

    public async Task<OtpGenerationResult> GenerateAndSendAsync(
        string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        // Check cooldown
        var latestOtp = await _otpRepository.GetLatestByMobileAndPurposeAsync(
            normalizedMobileNumber, purpose, cancellationToken);

        if (latestOtp != null)
        {
            var cooldownEnd = latestOtp.CreatedAt.AddSeconds(_securityOptions.OtpResendCooldownSeconds);
            if (IndianStandardTime.Now < cooldownEnd)
            {
                return new OtpGenerationResult(false, null,
                    ApiErrorCodes.OtpResendCooldown,
                    $"Please wait before requesting another OTP.");
            }
        }

        // Generate cryptographically secure OTP
        bool demo = _otpOptions.Mode == OtpMode.Demo;
        var otpCode = demo ? OtpOptions.DemoCode : GenerateSecureOtp(_securityOptions.OtpLength);
        var otpHash = HashOtp(otpCode);

        var otpRequest = new OtpRequest
        {
            ReferenceId = Guid.NewGuid(),
            MobileNumber = normalizedMobileNumber,
            OtpHash = otpHash,
            Purpose = purpose,
            AttemptCount = 0,
            IsVerified = false,
            ExpiresAt = IndianStandardTime.Now.AddSeconds(_securityOptions.OtpExpirationSeconds),
            CreatedAt = IndianStandardTime.Now
        };

        await _otpRepository.AddAsync(otpRequest, cancellationToken);

        if (demo)
        {
            // Demo mode: nothing is sent, and the OTP is neither logged nor claimed to have been delivered.
            _logger.LogWarning("DEMO OTP MODE: fixed OTP issued without SMS for purpose {Purpose}, reference {ReferenceId}", purpose, otpRequest.ReferenceId);
            return new OtpGenerationResult(true, otpRequest.ReferenceId, ExpiresAtIst: otpRequest.ExpiresAt, IsDemo: true);
        }

        // Send OTP via the SMS gateway. A failure is a failure: there is deliberately no fallback to the demo code.
        var smsResult = await _smsService.SendOtpAsync(normalizedMobileNumber, otpCode, cancellationToken: cancellationToken);
        if (!smsResult.Success)
        {
            _logger.LogError("SMS sending failed for OTP purpose {Purpose}: {Error}", purpose, smsResult.ErrorMessage);
            return new OtpGenerationResult(false, null, ApiErrorCodes.OtpSendFailed, "Failed to send OTP via SMS.");
        }

        // Do NOT log the OTP value
        _logger.LogInformation("OTP sent successfully for purpose {Purpose}, reference {ReferenceId}", purpose, otpRequest.ReferenceId);

        return new OtpGenerationResult(true, otpRequest.ReferenceId, ExpiresAtIst: otpRequest.ExpiresAt);
    }

    public async Task<OtpVerificationResult> VerifyAsync(
        Guid referenceId, string otpCode, OtpPurpose expectedPurpose, CancellationToken cancellationToken = default)
    {
        var otpRequest = await _otpRepository.GetByReferenceIdAsync(referenceId, cancellationToken);

        if (otpRequest == null)
            return new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "Invalid OTP reference.");

        // Purpose isolation
        if (otpRequest.Purpose != expectedPurpose)
            return new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "Invalid OTP reference.");

        // Already verified
        if (otpRequest.IsVerified)
            return new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "OTP has already been used.");

        // Expiration check
        if (IndianStandardTime.Now > otpRequest.ExpiresAt)
            return new OtpVerificationResult(false, ApiErrorCodes.OtpExpired, "OTP has expired.");

        // Attempt limit check
        if (otpRequest.AttemptCount >= _securityOptions.OtpMaxAttempts)
            return new OtpVerificationResult(false, ApiErrorCodes.OtpMaxAttemptsExceeded, "Maximum verification attempts exceeded.");

        // Increment attempt count
        otpRequest.AttemptCount++;

        // Verify hash
        var inputHash = HashOtp(otpCode);
        if (!string.Equals(inputHash, otpRequest.OtpHash, StringComparison.Ordinal))
        {
            await _otpRepository.UpdateAsync(otpRequest, cancellationToken);
            return new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "Invalid OTP code.");
        }

        // Success — mark as verified
        otpRequest.IsVerified = true;
        otpRequest.VerifiedAt = IndianStandardTime.Now;
        await _otpRepository.UpdateAsync(otpRequest, cancellationToken);

        return new OtpVerificationResult(true);
    }

    private static string GenerateSecureOtp(int length)
    {
        var max = (int)Math.Pow(10, length);
        var otp = RandomNumberGenerator.GetInt32(0, max);
        return otp.ToString().PadLeft(length, '0');
    }

    private static string HashOtp(string otp)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(otp);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
