using NSG.Mobile.Application.Interfaces.Infrastructure;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>Fixed at startup by <c>AddR2StorageWhenConfigured</c>: available in Development, or when R2 is the configured provider.</summary>
public sealed class StorageAvailability : IStorageAvailability
{
    public StorageAvailability(bool isAvailable) => IsAvailable = isAvailable;

    public bool IsAvailable { get; }
}
