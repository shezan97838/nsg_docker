namespace NSG.Mobile.Application.Admin;

/// <summary>Human-readable messages of the Admin Portal flows. None names a record other than the caller's, a secret or personal data.</summary>
public static class AdminMessages
{
    public const string NotMatched = "We could not find a matching record for the details provided. Please check and try again.";
    public const string InvalidCredentials = "Invalid credentials.";
    public const string AccessUnavailable = "Your access is unavailable. Please contact the Super Admin.";
    public const string AccountLocked = "Your account is temporarily locked because of too many failed attempts. Please try again later.";
    public const string SessionInvalid = "Your session is not valid. Please sign in again.";
    public const string SessionExpired = "Your session has expired. Please sign in again.";
    public const string SessionEnded = "Your session was ended for security reasons. Please sign in again.";

    public const string OtpSent = "A one-time password has been sent to your registered mobile number.";
    public const string DemoOtpSent = "Demo OTP mode is active. No SMS has been sent; use the demonstration OTP provided for this presentation.";
    public const string OtpRateLimited = "Too many OTP requests. Please try again later.";
    public const string OtpReferenceInvalid = "Invalid OTP reference.";

    public const string AccountCreated = "Admin account created successfully. The person can now activate it with an OTP sent to the mobile number registered in PIS.";
    public const string AccountActivated = "Your admin account has been activated. You can now sign in with your password and an OTP.";
    public const string AccountDeactivated = "Admin account deactivated successfully.";
    public const string AccountAlreadyInactive = "Admin account is already inactive.";
    public const string AccountReactivated = "Admin account reactivated successfully.";
    public const string AccountAlreadyActive = "Admin account is already active.";
    public const string AccountNotFound = "Admin account not found.";
    public const string NoAccountsFound = "No admin accounts found.";
    public const string NoRolesFound = "No admin roles found.";
    public const string RolesFound = "Admin roles retrieved successfully.";
    public const string CannotDeactivateSelf = "You cannot deactivate your own account.";
    public const string LastSuperAdmin = "The last active Super Admin cannot be deactivated.";

    public const string LoginSuccessful = "Login successful.";
    public const string SessionRefreshed = "Session refreshed.";
    public const string LoggedOut = "You have been signed out.";
    public const string ProfileRetrieved = "Admin profile retrieved successfully.";

    public const string PersonnelNotFound = "No active personnel record matches the NSG number, mobile number and date of birth provided.";
    public const string AccountExists = "An admin account already exists for this NSG number.";
    public const string RoleInvalid = "The role is not valid.";
}
