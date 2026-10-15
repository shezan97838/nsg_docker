namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a user account.
/// SOW §3.7: Secure Onboarding flow defines the registration → approval → active lifecycle.
/// </summary>
public enum UserStatus
{
    /// <summary>Registration submitted, OTP not yet verified.</summary>
    PendingOtpVerification = 0,

    /// <summary>OTP verified, awaiting IT Cell approval.</summary>
    PendingITApproval = 1,

    /// <summary>IT Cell approved, account fully active.</summary>
    Active = 2,

    /// <summary>Account locked due to excessive failed login attempts.</summary>
    Locked = 3,

    /// <summary>Account suspended by administrative action.</summary>
    Suspended = 4
}
