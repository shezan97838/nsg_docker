namespace NSG.Mobile.Application.Common;

/// <summary>
/// Centralized error codes for consistent API error responses.
/// Maps to ProblemDetails type/title fields.
/// </summary>
public static class ApiErrorCodes
{
    // Authentication errors
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AccountNotActive = "AUTH_ACCOUNT_NOT_ACTIVE";
    public const string AccountLocked = "AUTH_ACCOUNT_LOCKED";
    public const string AccountSuspended = "AUTH_ACCOUNT_SUSPENDED";
    public const string AccountPendingApproval = "AUTH_PENDING_APPROVAL";

    // OTP errors
    public const string OtpExpired = "OTP_EXPIRED";
    public const string OtpInvalid = "OTP_INVALID";
    public const string OtpMaxAttemptsExceeded = "OTP_MAX_ATTEMPTS";
    public const string OtpResendCooldown = "OTP_RESEND_COOLDOWN";
    public const string OtpSendFailed = "OTP_SEND_FAILED";

    // Registration errors
    public const string MobileNumberAlreadyRegistered = "REG_MOBILE_EXISTS";
    public const string ForceNumberAlreadyRegistered = "REG_FORCE_NUMBER_EXISTS";
    public const string RegistrationAlreadyPending = "REG_ALREADY_PENDING";

    // Mobile registration / MPIN errors
    public const string MobileNotMatched = "MOBILE_NOT_MATCHED";
    public const string OtpRateLimited = "OTP_RATE_LIMITED";
    public const string GrantInvalid = "GRANT_INVALID";

    // Token errors
    public const string RefreshTokenInvalid = "TOKEN_INVALID";
    public const string RefreshTokenExpired = "TOKEN_EXPIRED";
    public const string RefreshTokenRevoked = "TOKEN_REVOKED";
    public const string RefreshTokenReuseDetected = "TOKEN_REUSE_DETECTED";

    // Device errors
    public const string DeviceNotRegistered = "DEVICE_NOT_REGISTERED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string DeviceMismatch = "DEVICE_MISMATCH";

    // Authorization errors
    public const string Forbidden = "AUTH_FORBIDDEN";
    public const string Unauthorized = "AUTH_UNAUTHORIZED";

    // General errors
    public const string NotFound = "NOT_FOUND";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Conflict = "CONFLICT";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
}
