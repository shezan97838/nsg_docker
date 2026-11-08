using NSG.Mobile.Application.Interfaces.Infrastructure;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>Fixed at startup: the local storage root is validated before the host starts, so production registers it as available.</summary>
public sealed class StorageAvailability : IStorageAvailability
{
    public StorageAvailability(bool isAvailable) => IsAvailable = isAvailable;

    public bool IsAvailable { get; }
}
