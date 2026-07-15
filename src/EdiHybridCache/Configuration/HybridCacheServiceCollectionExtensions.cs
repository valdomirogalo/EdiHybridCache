using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Configuration;

public static class HybridCacheServiceCollectionExtensions
{
    public const string ConfigurationSectionName = "EdiHybridCache";

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
            TryOverrideFromEnv("REDIS_CONNECTION", v => options.RedisConnectionString = v);
            TryOverrideFromEnv("RABBITMQ_HOST", v => options.RabbitMqHost = v);
            TryOverrideFromEnv("RABBITMQ_USERNAME", v => options.RabbitMqUsername = v);
            TryOverrideFromEnv("RABBITMQ_PASSWORD", v => options.RabbitMqPassword = v);

            TryParseEnvInt("L1_TTL_SECONDS", v => options.L1TtlSeconds = v);
            TryParseEnvInt("DEFAULT_L2_TTL_SECONDS", v => options.DefaultL2TtlSeconds = v);
            TryParseEnvDouble("L2_TTL_MULTIPLIER", v => options.L2TtlMultiplier = v);
        });

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<HybridCacheOptions>>().Value;
            if (string.IsNullOrEmpty(opts.RedisConnectionString))
                throw new InvalidOperationException("Redis connection string is not configured.");
            return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });

        services.AddMemoryCache();
        services.AddSingleton<ICacheInvalidationPublisher, RabbitMqInvalidationPublisher>();
        services.AddSingleton<ICacheInvalidationSubscriber, RabbitMqInvalidationSubscriber>();
        services.AddScoped<IHybridCache, HybridCache>();

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
