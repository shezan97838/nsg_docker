namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Whether stored files may be read and written in this deployment. It is false outside Development when private object storage (R2) is not
/// the active provider, so a production host can never fall back to its local disk for user documents.
/// </summary>
public interface IStorageAvailability
{
    bool IsAvailable { get; }
}
