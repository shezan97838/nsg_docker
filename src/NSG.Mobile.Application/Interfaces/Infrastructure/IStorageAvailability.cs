namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Whether stored files may be read and written in this deployment. The local storage root is validated at startup, so the production
/// implementation always reports true; the flag is kept so services answer 503 (not an unhandled error) when a host reports storage as unavailable.
/// </summary>
public interface IStorageAvailability
{
    bool IsAvailable { get; }
}
