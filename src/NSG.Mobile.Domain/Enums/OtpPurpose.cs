namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Classifies the purpose of an OTP request to prevent cross-purpose reuse.
/// SOW §3.7: OTP for registration. §3.8: OTP as part of MFA login.
/// </summary>
public enum OtpPurpose
{
    /// <summary>OTP for initial user registration (SOW §3.7 Step 1).</summary>
    Registration = 0,

    /// <summary>OTP for login MFA (SOW §3.8: Password + OTP + Biometric).</summary>
    Login = 1,

    /// <summary>OTP for registering a new/replacement device.</summary>
    DeviceRegistration = 2,

    /// <summary>OTP for password reset flow.</summary>
    PasswordReset = 3,

    /// <summary>OTP for sensitive data change (e.g., PIS change request approval).</summary>
    SensitiveChange = 4,

    /// <summary>OTP that proves control of the PIS-registered mobile while activating an Admin Portal account.</summary>
    AdminActivation = 5,

    /// <summary>OTP for the second factor of an Admin Portal login (password + OTP).</summary>
    AdminLogin = 6
}
