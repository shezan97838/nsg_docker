namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Approval state of a person's request to activate the mobile app.
/// Values are numeric and must not change once persisted.
/// </summary>
public enum MobileAppActivationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
