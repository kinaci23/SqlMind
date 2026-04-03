using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using SqlMind.Core.Interfaces;
using System.Text.Json;

namespace SqlMind.Infrastructure.Cache;

/// <summary>
/// ICacheService implementation backed by Redis (StackExchange.Redis).
/// Cache key for LLM responses: SHA-256(sql_content) — identical SQL never triggers a second LLM call.
/// Default TTL for LLM responses: 1 hour.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue)
                return default;

            return JsonSerializer.Deserialize<T>(value!, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache GET failed for key={Key} — treating as miss.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json    = JsonSerializer.Serialize(value, _jsonOpts);
            var expiry  = ttl ?? DefaultTtl;
            await _db.StringSetAsync(key, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SET failed for key={Key} — continuing without cache.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache REMOVE failed for key={Key}.", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache EXISTS failed for key={Key} — returning false.", key);
            return false;
        }
    }
}
