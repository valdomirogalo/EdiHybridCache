using System.Diagnostics.Metrics;

namespace EdiHybridCache.Cache;

/// <summary>
/// Provides meters and counters for hybrid cache operations visible to Aspire / OpenTelemetry.
/// </summary>
public sealed class CacheMetrics : IDisposable
{
    /// <summary>
    /// Meter name used to identify EdiHybridCache metrics in OpenTelemetry.
    /// </summary>
    /// <summary>
    /// Meter name used to identify EdiHybridCache metrics in OpenTelemetry.
    /// Sourced from Constants.MeterName.
    /// </summary>
    public const string MeterName = Constants.MeterName;

    private readonly Meter _meter;
    private long _currentCacheSize;

    public CacheMetrics()
    {
        _meter = new Meter(MeterName);

        L1Hits = _meter.CreateCounter<long>(
            Constants.MetricL1Hits,
            description: "Number of L1 (memory) cache hits");

        L2Hits = _meter.CreateCounter<long>(
            Constants.MetricL2Hits,
            description: "Number of L2 (Redis) cache hits (and L1 repopulated)");

        CacheMisses = _meter.CreateCounter<long>(
            Constants.MetricCacheMisses,
            description: "Number of full cache misses (L1 + L2)");

        RedisOperations = _meter.CreateCounter<long>(
            Constants.MetricRedisOperations,
            description: "Number of Redis operations executed");

        SetOperations = _meter.CreateCounter<long>(
            Constants.MetricSetOperations,
            description: "Number of cache set operations");

        RemoveOperations = _meter.CreateCounter<long>(
            Constants.MetricRemoveOperations,
            description: "Number of cache remove operations");

        InvalidationsPublished = _meter.CreateCounter<long>(
            Constants.MetricInvalidationsPublished,
            description: "Number of invalidation events published via RabbitMQ");

        CurrentCacheSize = _meter.CreateObservableGauge<long>(
            Constants.MetricCacheSize,
            () => Interlocked.Read(ref _currentCacheSize),
            description: "Estimated number of entries in L1 cache");
    }

    // ── Counters ──────────────────────────────────────────
    public Counter<long> L1Hits { get; }
    public Counter<long> L2Hits { get; }
    public Counter<long> CacheMisses { get; }
    public Counter<long> RedisOperations { get; }
    public Counter<long> SetOperations { get; }
    public Counter<long> RemoveOperations { get; }
    public Counter<long> InvalidationsPublished { get; }

    /// <summary>
    /// Observable gauge reflecting the tracked L1 entry count.
    /// </summary>
    public ObservableGauge<long> CurrentCacheSize { get; }

    /// <summary>
    /// Updates the observed L1 cache size (called periodically or on mutations).
    /// </summary>
    public void SetCurrentCacheSize(long size)
    {
        Interlocked.Exchange(ref _currentCacheSize, size);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
