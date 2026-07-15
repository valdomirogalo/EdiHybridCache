namespace EdiHybridCache.Cache;

public class HybridCacheOptions
{
    public const int DefaultL1TtlSecondsValue = 300;
    public const int DefaultL2TtlSecondsValue = 3600;
    public const double DefaultL2TtlMultiplierValue = 1.5;
    public const int DefaultMaxCacheSizeBytesValue = 1024 * 1024;
    public const int DefaultCompressionThresholdBytesValue = 1024;
    public const int DefaultRetryCountValue = 3;
    public const int DefaultRetryBaseDelaySecondsValue = 1;

    public string RedisConnectionString { get; set; } = string.Empty;
    public string RabbitMqHost { get; set; } = "localhost";
    public string RabbitMqUsername { get; set; } = "guest";
    public string RabbitMqPassword { get; set; } = "guest";
    public string InvalidationExchange { get; set; } = "edi.cache.invalidation";
    public string InvalidationQueueName { get; set; } = string.Empty;

    // CWE-295 (CVSS 7.4): SSL/TLS settings for RabbitMQ
    public bool RabbitMqUseSsl { get; set; }
    public string RabbitMqSslServerName { get; set; } = string.Empty;
    public string RabbitMqSslCertificatePath { get; set; } = string.Empty;

    public int L1TtlSeconds { get; set; } = DefaultL1TtlSecondsValue;
    public int DefaultL2TtlSeconds { get; set; } = DefaultL2TtlSecondsValue;
    public double L2TtlMultiplier { get; set; } = DefaultL2TtlMultiplierValue;
    public int MaxCacheSizeBytes { get; set; } = DefaultMaxCacheSizeBytesValue;
    public bool EnableCompression { get; set; } = true;
    public int CompressionThresholdBytes { get; set; } = DefaultCompressionThresholdBytesValue;
    public int RedisOperationTimeoutSeconds { get; set; } = 5;
    public int RetryCount { get; set; } = DefaultRetryCountValue;
    public int RetryBaseDelaySeconds { get; set; } = DefaultRetryBaseDelaySecondsValue;
}
