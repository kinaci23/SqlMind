using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SqlMind.Core.Interfaces;

namespace SqlMind.Infrastructure.Cache;

public static class CacheServiceExtensions
{
    /// <summary>
    /// Registers Redis connection + RedisCacheService.
    /// Reads connection string from REDIS_URL or ConnectionStrings:Redis config key.
    /// Falls back to localhost:6379 when not configured (development convenience).
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var cs = config["REDIS_URL"]
                  ?? config.GetConnectionString("Redis")
                  ?? "localhost:6379";

            return ConnectionMultiplexer.Connect(cs);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
