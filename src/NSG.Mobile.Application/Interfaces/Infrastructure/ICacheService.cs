namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Distributed cache abstraction.
/// SOW §4: "Redis or equivalent distributed cache."
/// Used for OTP rate limiting, session state, and frequently accessed data.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
    Task SetStringAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<long> IncrementAsync(string key, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}
