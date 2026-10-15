using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>
/// Repository abstraction for User entity operations.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<User?> GetByMobileNumberAsync(string normalizedMobileNumber, CancellationToken cancellationToken = default);
    Task<User?> GetByForceNumberAsync(string forceNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByMobileNumberAsync(string normalizedMobileNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByForceNumberAsync(string forceNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetByStatusAsync(UserStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(UserStatus status, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
