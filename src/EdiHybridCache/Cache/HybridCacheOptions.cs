namespace EdiHybridCache.Cache;

public class HybridCacheOptions
{
    // Default values sourced from Constants.cs — single source of truth
    public string RedisConnectionString { get; set; } = Constants.DefaultRedisConnectionString;
    public string RabbitMqHost { get; set; } = Constants.DefaultRabbitMqHost;
    public int RabbitMqPort { get; set; } = Constants.DefaultRabbitMqPort;
    public string RabbitMqUsername { get; set; } = Constants.DefaultRabbitMqUsername;
    public string RabbitMqPassword { get; set; } = Constants.DefaultRabbitMqPassword;
    public string InvalidationExchange { get; set; } = Constants.DefaultInvalidationExchange;
    public string InvalidationQueueName { get; set; } = string.Empty;

    // CWE-295 (CVSS 7.4): SSL/TLS settings for RabbitMQ
    public bool RabbitMqUseSsl { get; set; }
    public string RabbitMqSslServerName { get; set; } = string.Empty;
    public string RabbitMqSslCertificatePath { get; set; } = string.Empty;

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
