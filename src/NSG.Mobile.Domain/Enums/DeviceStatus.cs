namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a registered device.
/// SOW §3.8: Single device binding per user with secure device management.
/// </summary>
public enum DeviceStatus
{
    /// <summary>Device registration initiated, pending full authentication.</summary>
    Pending = 0,

    /// <summary>Device is active and bound to the user.</summary>
    Active = 1,

    /// <summary>Device has been revoked (replaced or administratively removed).</summary>
    Revoked = 2
}
