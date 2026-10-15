using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>
/// Repository abstraction for RefreshToken operations.
/// Supports rotation chain management and reuse detection.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetByFamilyIdAsync(Guid familyId, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task RevokeAllByUserIdAsync(int userId, string reason, string? ipAddress, CancellationToken cancellationToken = default);
    Task RevokeAllByDeviceIdAsync(int deviceId, string reason, string? ipAddress, CancellationToken cancellationToken = default);
    Task RevokeFamilyAsync(Guid familyId, string reason, string? ipAddress, CancellationToken cancellationToken = default);
}
