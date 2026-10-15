using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth.DTOs;

namespace NSG.Mobile.Application.MobileAuth.Services;

/// <summary>Mobile registration, OTP, MPIN and login workflow. See <see cref="MobileAuthService"/>.</summary>
public interface IMobileAuthService
{
    /// <summary>Decides the next step for a person from their NSG number, mobile number and date of birth.</summary>
    Task<Result<MobileAuthFlowDto>> CheckAsync(MobileLoginCheckRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Verifies the MPIN of an active profile and issues the access and refresh tokens (a new session; any earlier session ends).</summary>
    Task<Result<MobileLoginResponseDto>> LoginAsync(MobileLoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a new access and refresh token (rotation). A reused token ends the whole session.</summary>
    Task<Result<MobileLoginResponseDto>> RefreshAsync(MobileRefreshRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Ends the session of a refresh token. Idempotent: the answer does not reveal whether the token existed.</summary>
    Task<Result<MobileAuthFlowDto>> LogoutAsync(MobileLogoutRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Sends an OTP to the registered mobile number once the person is eligible for the requested flow.</summary>
    Task<Result<MobileAuthFlowDto>> SendOtpAsync(MobileOtpSendRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Verifies the OTP, then raises the approval request, or issues the registration / reset grant.</summary>
    Task<Result<MobileAuthFlowDto>> VerifyOtpAsync(MobileOtpVerifyRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Redeems a registration grant after rechecking PIS, and returns the MPIN-setup grant for an approved person.</summary>
    Task<Result<MobileAuthFlowDto>> SubmitRegistrationAsync(RegistrationSubmitRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Redeems the MPIN-setup grant and creates the Employee profile.</summary>
    Task<Result<MobileAuthFlowDto>> SetupMpinAsync(MpinSetupRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Changes the MPIN of the authenticated profile after verifying the current one.</summary>
    Task<Result<MobileAuthFlowDto>> ChangeMpinAsync(MpinChangeRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Sets a new MPIN with an MPIN-reset grant and invalidates the person's other outstanding grants.</summary>
    Task<Result<MobileAuthFlowDto>> ResetMpinAsync(MpinResetRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
}
