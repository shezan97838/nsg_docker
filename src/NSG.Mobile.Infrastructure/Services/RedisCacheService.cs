using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NSG.Mobile.Application.Interfaces.Infrastructure;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Redis-backed distributed cache implementation.
/// SOW §4: "Redis or equivalent distributed cache."
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RedisCacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var data = await _cache.GetStringAsync(key, cancellationToken);
        return data == null ? null : JsonSerializer.Deserialize<T>(data, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var options = new DistributedCacheEntryOptions();
        if (expiration.HasValue) options.AbsoluteExpirationRelativeToNow = expiration;

        var data = JsonSerializer.Serialize(value, JsonOptions);
        await _cache.SetStringAsync(key, data, options, cancellationToken);
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        => await _cache.GetStringAsync(key, cancellationToken);

    public async Task SetStringAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();
        if (expiration.HasValue) options.AbsoluteExpirationRelativeToNow = expiration;
        await _cache.SetStringAsync(key, value, options, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => await _cache.RemoveAsync(key, cancellationToken);

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var data = await _cache.GetAsync(key, cancellationToken);
        return data != null;
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        // IDistributedCache doesn't natively support INCR.
        // For production, use StackExchange.Redis directly for atomic increment.
        // This is a safe fallback using string operations.
        var current = await GetStringAsync(key, cancellationToken);
        var value = current != null ? long.Parse(current) + 1 : 1;
        await SetStringAsync(key, value.ToString(), expiration, cancellationToken);
        return value;
    }
}
