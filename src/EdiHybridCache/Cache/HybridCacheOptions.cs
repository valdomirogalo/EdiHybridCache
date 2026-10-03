namespace EdiHybridCache.Cache;

public class HybridCacheOptions
{
    // Default values sourced from Constants.cs — single source of truth
    public string RedisConnectionString { get; set; } = Constants.DefaultRedisConnectionString;

    /// <summary>Redis Pub/Sub channel used to broadcast L1 invalidation events across instances.</summary>
    public string InvalidationChannel { get; set; } = Constants.DefaultInvalidationChannel;

    public int L1TtlSeconds { get; set; } = Constants.DefaultL1TtlSeconds;
    public int DefaultL2TtlSeconds { get; set; } = Constants.DefaultL2TtlSeconds;
    public double L2TtlMultiplier { get; set; } = Constants.DefaultL2TtlMultiplier;
    public int MaxCacheSizeBytes { get; set; } = Constants.DefaultMaxCacheSizeBytes;
    public bool EnableCompression { get; set; } = true;
    public int CompressionThresholdBytes { get; set; } = Constants.DefaultCompressionThresholdBytes;
    public int RedisOperationTimeoutSeconds { get; set; } = Constants.DefaultRedisOperationTimeoutSeconds;
    public int RetryCount { get; set; } = Constants.DefaultRetryCount;
    public int RetryBaseDelaySeconds { get; set; } = Constants.DefaultRetryBaseDelaySeconds;
}
