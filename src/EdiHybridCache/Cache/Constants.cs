namespace EdiHybridCache.Cache;

/// <summary>
/// Central constants for EdiHybridCache — limits, defaults, metric names,
/// environment variables, and resource identifiers.
/// </summary>
public static class Constants
{
    // ─────────────────────────────────────────────────────────────────
    //  Cache Limits
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Maximum allowed cache key length in characters.</summary>
    public const int MaxKeyLength = 512;

    /// <summary>Maximum allowed serialized value size (100 MB).</summary>
    public const int MaxValueSizeBytes = 100 * 1024 * 1024;

    /// <summary>Default buffer size for JSON serialization.</summary>
    public const int JsonDefaultBufferSize = 4096;

    // ─────────────────────────────────────────────────────────────────
    //  Compression
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Hard cap for decompressed data (100 MB) — CWE-409 ZIP Bomb protection.</summary>
    public const int MaxDecompressedBytes = 100 * 1024 * 1024;

    /// <summary>GZip overhead margin for incompressible data (headers + checksum).</summary>
    public const int GzipOverhead = 256;

    // ─────────────────────────────────────────────────────────────────
    //  AsyncLock
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Number of stripes for the hash-based async lock.</summary>
    public const int AsyncLockStripeCount = 1024;

    /// <summary>Eviction percentage when MemoryCache size limit is reached.</summary>
    public const double CacheCompactionPercentage = 0.2;

    // ─────────────────────────────────────────────────────────────────
    //  Default Configuration Values
    // ─────────────────────────────────────────────────────────────────
    public const int DefaultL1TtlSeconds = 300;
    public const int DefaultL2TtlSeconds = 3600;
    public const double DefaultL2TtlMultiplier = 1.5;
    public const int DefaultMaxCacheSizeBytes = 1024 * 1024;
    public const int DefaultCompressionThresholdBytes = 1024;
    public const int DefaultRetryCount = 3;
    public const int DefaultRetryBaseDelaySeconds = 1;
    public const int DefaultRedisOperationTimeoutSeconds = 5;

    // ─────────────────────────────────────────────────────────────────
    //  Default Connection Values
    // ─────────────────────────────────────────────────────────────────
    public const string DefaultRabbitMqHost = "localhost";
    public const int DefaultRabbitMqPort = 5672;
    public const string DefaultRabbitMqUsername = "guest";
    public const string DefaultRabbitMqPassword = "guest";
    public const string DefaultInvalidationExchange = "edi.cache.invalidation";
    public const string DefaultRedisConnectionString = "localhost:6379";

    /// <summary>RabbitMQ heartbeat interval in seconds.</summary>
    public const int RabbitMqHeartbeatSeconds = 30;

    // ─────────────────────────────────────────────────────────────────
    //  Environment Variable Names
    // ─────────────────────────────────────────────────────────────────
    public const string EnvRedisConnection = "REDIS_CONNECTION";
    public const string EnvRabbitMqHost = "RABBITMQ_HOST";
    public const string EnvRabbitMqPort = "RABBITMQ_PORT";
    public const string EnvRabbitMqUsername = "RABBITMQ_USERNAME";
    public const string EnvRabbitMqPassword = "RABBITMQ_PASSWORD";
    public const string EnvL1TtlSeconds = "L1_TTL_SECONDS";
    public const string EnvDefaultL2TtlSeconds = "DEFAULT_L2_TTL_SECONDS";
    public const string EnvL2TtlMultiplier = "L2_TTL_MULTIPLIER";

    // ─────────────────────────────────────────────────────────────────
    //  OpenTelemetry Metric Names
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Meter name used by all EdiHybridCache metrics.</summary>
    public const string MeterName = "EdiHybridCache";

    // Metric instrument names
    public const string MetricL1Hits = "edi.cache.l1.hits";
    public const string MetricL2Hits = "edi.cache.l2.hits";
    public const string MetricCacheMisses = "edi.cache.misses";
    public const string MetricRedisOperations = "edi.cache.redis.operations";
    public const string MetricSetOperations = "edi.cache.set.operations";
    public const string MetricRemoveOperations = "edi.cache.remove.operations";
    public const string MetricInvalidationsPublished = "edi.cache.invalidations.published";
    public const string MetricCacheSize = "edi.cache.size";

    // ─────────────────────────────────────────────────────────────────
    //  Configuration Section / AppHost Resource Names
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string ConfigurationSectionName = "EdiHybridCache";

    /// <summary>Resource name for Redis in the Aspire AppHost.</summary>
    public const string AspireRedisName = "redis";

    /// <summary>Resource name for RabbitMQ in the Aspire AppHost.</summary>
    public const string AspireRabbitMqName = "rabbitmq";

    /// <summary>Resource name for the Playground project in the Aspire AppHost.</summary>
    public const string AspirePlaygroundName = "playground";

    // ─────────────────────────────────────────────────────────────────
    //  Connection String Suffixes
    // ─────────────────────────────────────────────────────────────────
    /// <summary>Suffix appended to Redis connection strings for DCP proxy TLS.</summary>
    public const string RedisSslSuffix = ",ssl=true,abortConnect=false,password=";
}
