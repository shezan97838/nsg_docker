using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>
/// Repository abstraction for Device entity operations.
/// </summary>
public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Device?> GetActiveDeviceByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<Device?> GetByUserIdAndIdentifierHashAsync(int userId, string deviceIdentifierHash, CancellationToken cancellationToken = default);
    Task AddAsync(Device device, CancellationToken cancellationToken = default);
    Task UpdateAsync(Device device, CancellationToken cancellationToken = default);
    Task RevokeAllUserDevicesAsync(int userId, CancellationToken cancellationToken = default);
}
