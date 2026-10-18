namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Activation state of an Admin Portal account (<c>admin_profiles.activation_status</c>).
/// Values are numeric and must not change once persisted.
/// </summary>
public enum AdminActivationStatus
{
    /// <summary>Provisioned by a Super Admin; the person has not yet verified the OTP and set a password. No password hash exists.</summary>
    PendingActivation = 0,

    /// <summary>The person verified the OTP and set a password. Only an Active, enabled account can sign in.</summary>
    Active = 1
}
