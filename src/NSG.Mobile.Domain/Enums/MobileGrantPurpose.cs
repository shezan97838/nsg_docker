namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// What a short-lived mobile authentication grant may be used for. A grant is an opaque, single-use secret that is
/// accepted by exactly one endpoint and is never a bearer (JWT) credential, so it cannot reach any protected API.
/// Values are numeric and must not change once persisted.
/// </summary>
public enum MobileGrantPurpose
{
    /// <summary>Issued after a successful registration OTP for a person whose PIS status is Approved; accepted by registration/submit.</summary>
    RegistrationVerified = 0,

    /// <summary>Issued by registration/submit; accepted by mpin/setup, which creates the profile.</summary>
    MpinSetup = 1,

    /// <summary>Issued after a successful MPIN-recovery OTP; accepted by mpin/reset.</summary>
    MpinReset = 2
}
