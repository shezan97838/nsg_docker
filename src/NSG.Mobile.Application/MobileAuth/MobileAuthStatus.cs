namespace NSG.Mobile.Application.MobileAuth;

/// <summary>Machine-readable next-step codes returned in <c>data.status</c> by the mobile-auth endpoints.</summary>
public static class MobileAuthStatus
{
    public const string MpinRequired = "MPIN_REQUIRED";
    public const string OtpRequired = "OTP_REQUIRED";
    public const string OtpSent = "OTP_SENT";
    public const string RequestPending = "REQUEST_PENDING";
    public const string ApprovalRequested = "APPROVAL_REQUESTED";
    public const string AccessUnavailable = "ACCESS_UNAVAILABLE";
    public const string RegistrationVerified = "REGISTRATION_VERIFIED";
    public const string MpinSetupGranted = "MPIN_SETUP_GRANTED";
    public const string ProfileCreated = "PROFILE_CREATED";
    public const string MpinResetGranted = "MPIN_RESET_GRANTED";
    public const string MpinChanged = "MPIN_CHANGED";
    public const string MpinReset = "MPIN_RESET";
    public const string LoggedOut = "LOGGED_OUT";
}

/// <summary>User-facing messages fixed by the business rules.</summary>
public static class MobileAuthMessages
{
    public const string ApprovalRaised = "Your approval request has been raised successfully.";
    public const string RequestAlreadyPending = "Your request is already pending. Please wait for approval.";
    public const string AccessUnavailable = "Your access is unavailable. Please contact the IT Admin.";

    /// <summary>Shown with the machine-readable code <c>SURAKSHA_1001</c>: access was blocked by a PIS Admin.</summary>
    public const string AccessBlockedByPisAdmin = "Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.";

    /// <summary>Shown with the machine-readable code <c>SURAKSHA_1002</c>: the session was replaced by a newer login of the same account.</summary>
    public const string SessionReplacedByNewLogin = "Your session has been terminated because your account was signed in on another device.";

    /// <summary>The client-safe message of a public session code (<c>SURAKSHA_1001</c> / <c>SURAKSHA_1002</c>); null for any other code.</summary>
    public static string? ForSessionCode(string? code) => code switch
    {
        Common.ApiErrorCodes.MobileAccessBlocked => AccessBlockedByPisAdmin,
        Common.ApiErrorCodes.MobileSessionReplaced => SessionReplacedByNewLogin,
        _ => null
    };

    public const string NotMatched = "We could not find a matching record for the details provided. Please check and try again.";
    public const string InvalidCredentials = "Invalid credentials.";
    public const string AccountLocked = "Your account is temporarily locked because of too many failed attempts. Please try again later.";
    public const string GrantInvalid = "This step has expired or is no longer valid. Please start again.";
    public const string SessionInvalid = "Your session is not valid. Please sign in again.";
    public const string SessionExpired = "Your session has expired. Please sign in again.";
    public const string SessionEnded = "Your session was ended for security reasons. Please sign in again.";
    public const string DemoOtpSent = "Demo OTP mode is active. No SMS has been sent; use the demonstration OTP provided for this presentation.";

    /// <summary>The default message for a flow status.</summary>
    public static string For(string status) => status switch
    {
        MobileAuthStatus.MpinRequired => "Please enter your MPIN to continue.",
        MobileAuthStatus.OtpRequired => "A one-time password is required to continue.",
        MobileAuthStatus.OtpSent => "A one-time password has been sent to your registered mobile number.",
        MobileAuthStatus.RequestPending => RequestAlreadyPending,
        MobileAuthStatus.ApprovalRequested => ApprovalRaised,
        MobileAuthStatus.AccessUnavailable => AccessUnavailable,
        MobileAuthStatus.RegistrationVerified => "Mobile number verified. Please continue with your registration.",
        MobileAuthStatus.MpinSetupGranted => "Registration verified. Please set your MPIN.",
        MobileAuthStatus.ProfileCreated => "Your MPIN has been set. You can now log in.",
        MobileAuthStatus.MpinResetGranted => "Mobile number verified. Please set a new MPIN.",
        MobileAuthStatus.MpinChanged => "Your MPIN has been changed successfully.",
        MobileAuthStatus.MpinReset => "Your MPIN has been reset successfully. You can now log in.",
        MobileAuthStatus.LoggedOut => "You have been logged out.",
        _ => "Request processed."
    };
}
