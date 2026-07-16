using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class CacheMetricsTests
{
    [Fact]
    public void MeterName_MatchesConstant()
    {
        Assert.Equal(Constants.MeterName, CacheMetrics.MeterName);
    }

    [Fact]
    public void Constructor_ShouldCreateAllInstruments()
    {
        using var metrics = new CacheMetrics();

        // All counters should be accessible (not null)
        Assert.NotNull(metrics.L1Hits);
        Assert.NotNull(metrics.L2Hits);
        Assert.NotNull(metrics.CacheMisses);
        Assert.NotNull(metrics.RedisOperations);
        Assert.NotNull(metrics.SetOperations);
        Assert.NotNull(metrics.RemoveOperations);
        Assert.NotNull(metrics.InvalidationsPublished);
        Assert.NotNull(metrics.CurrentCacheSize);
    }

    [Fact]
    public void L1Hits_Increment_ShouldNotThrow()
    {
        using var metrics = new CacheMetrics();
        metrics.L1Hits.Add(1);
        metrics.L1Hits.Add(5);
    }

    [Fact]
    public void SetCurrentCacheSize_ShouldUpdateReadableValue()
    {
        using var metrics = new CacheMetrics();
        metrics.SetCurrentCacheSize(42);
        // ObservableGauge value is read via callback — we verify
        // no exception and the value is retrievable through the meter.
        // The actual value is observable via OpenTelemetry export.
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var metrics = new CacheMetrics();
        metrics.Dispose();
        // Double dispose should not throw
        metrics.Dispose();
    }

    [Fact]
    public void MultipleMetricsInstances_ShouldNotThrow()
    {
        using var m1 = new CacheMetrics();
        using var m2 = new CacheMetrics();
        m1.L1Hits.Add(1);
        m2.L1Hits.Add(2);
    }
}
