using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Configuration;

public static class HybridCacheServiceCollectionExtensions
{
    /// <summary>
    /// Configuration section name sourced from Constants.ConfigurationSectionName.
    /// </summary>
    public const string ConfigurationSectionName = Constants.ConfigurationSectionName;

    // Covered by: ConfigurationTests (x5)
    public static IServiceCollection AddEdiHybridCache(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        services.Configure<HybridCacheOptions>(configuration.GetSection(ConfigurationSectionName));
        if (configureOptions != null)
            services.Configure(configureOptions);

        services.PostConfigure<HybridCacheOptions>(options =>
        {
            TryOverrideFromEnv(Constants.EnvRedisConnection, v => options.RedisConnectionString = v);
            TryOverrideFromEnv(Constants.EnvRabbitMqHost, v => options.RabbitMqHost = v);
            TryParseEnvInt(Constants.EnvRabbitMqPort, v => options.RabbitMqPort = v);
            TryOverrideFromEnv(Constants.EnvRabbitMqUsername, v => options.RabbitMqUsername = v);
            TryOverrideFromEnv(Constants.EnvRabbitMqPassword, v => options.RabbitMqPassword = v);

            TryParseEnvInt(Constants.EnvL1TtlSeconds, v => options.L1TtlSeconds = v);
            TryParseEnvInt(Constants.EnvDefaultL2TtlSeconds, v => options.DefaultL2TtlSeconds = v);
            TryParseEnvDouble(Constants.EnvL2TtlMultiplier, v => options.L2TtlMultiplier = v);
        });

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<HybridCacheOptions>>().Value;
            if (string.IsNullOrEmpty(opts.RedisConnectionString))
                throw new InvalidOperationException("Redis connection string is not configured.");

            var config = ConfigurationOptions.Parse(opts.RedisConnectionString);
            config.AbortOnConnectFail = false;
            config.ConnectTimeout = opts.RedisOperationTimeoutSeconds * 1000;
            config.SyncTimeout = opts.RedisOperationTimeoutSeconds * 1000;
            config.ConnectRetry = 3;
            config.ReconnectRetryPolicy = new LinearRetry(1000);
            config.KeepAlive = 60;
            config.ConfigCheckSeconds = 0;
            config.TieBreaker = "";

            return ConnectionMultiplexer.Connect(config);
        });

        // Metrics
        services.AddSingleton<CacheMetrics>();

        // L1 (memory) cache: SizeLimit disabled by default because the L1 TTL (60 s in
        // playground, 300 s default) already bounds entry lifetime. Under 5K concurrent VUs
        // with SizeLimit = 1 MB, the cache was thrashing (constant compaction), forcing every
        // request to hit Redis (L2, ~750 μs) instead of L1 (~1,2 μs).
        // If an explicit SizeLimit is desired, configure MaxCacheSizeBytes in appsettings.
        services.AddSingleton<MemoryCacheOptions>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<HybridCacheOptions>>().Value;
            return new MemoryCacheOptions
            {
                SizeLimit = opts.MaxCacheSizeBytes > 0 ? opts.MaxCacheSizeBytes : null,
                CompactionPercentage = Constants.CacheCompactionPercentage
            };
        });
        services.AddSingleton<IMemoryCache, MemoryCache>();
        services.AddSingleton<ICacheInvalidationPublisher, RabbitMqInvalidationPublisher>();
        services.AddSingleton<ICacheInvalidationSubscriber, RabbitMqInvalidationSubscriber>();

        // Singleton: HybridCache has no per-request state (key/value are method params).
        // All injected dependencies (IMemoryCache, IConnectionMultiplexer, etc.) are also
        // singletons. Scoped registration caused each HTTP request to create its own
        // AsyncLock (16,384 SemaphoreSlim × concurrent requests) which wasted memory and
        // defeated cross-request stampede protection.
        services.AddSingleton<IHybridCache, HybridCache>();

        return services;
    }

    // Covered by: UseEdiHybridCacheSubscriberAsync_ShouldStartSubscriber (x1)
    public static async Task UseEdiHybridCacheSubscriberAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var subscriber = serviceProvider.GetRequiredService<ICacheInvalidationSubscriber>();
        await subscriber.StartAsync(cancellationToken);
    }

    private static void TryOverrideFromEnv(string envVar, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(value))
            setter(value);
    }

    private static void TryParseEnvInt(string envVar, Action<int> setter)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (int.TryParse(value, out int parsed))
            setter(parsed);
    }

    private static void TryParseEnvDouble(string envVar, Action<double> setter)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (double.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            setter(parsed);
    }
}
