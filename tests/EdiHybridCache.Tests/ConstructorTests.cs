using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Tests;

public class ConstructorTests
{
    private readonly IMemoryCache _memoryCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheInvalidationPublisher _publisher;
    private readonly CacheMetrics _metrics;
    private readonly IOptions<HybridCacheOptions> _options;
    private readonly ILogger<HybridCache> _logger;

    public ConstructorTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _redis = new Mock<IConnectionMultiplexer>().Object;
        _publisher = new Mock<ICacheInvalidationPublisher>().Object;
        _metrics = new CacheMetrics();
        _options = new OptionsWrapper<HybridCacheOptions>(new HybridCacheOptions());
        _logger = NullLogger<HybridCache>.Instance;
    }

    [Fact]
    public void Constructor_WhenMemoryCacheIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(null!, _redis, _publisher, _metrics, _options, _logger);
        Assert.Throws<ArgumentNullException>("memoryCache", act);
    }

    [Fact]
    public void Constructor_WhenRedisConnectionIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(_memoryCache, null!, _publisher, _metrics, _options, _logger);
        Assert.Throws<ArgumentNullException>("redisConnection", act);
    }

    [Fact]
    public void Constructor_WhenPublisherIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(_memoryCache, _redis, null!, _metrics, _options, _logger);
        Assert.Throws<ArgumentNullException>("publisher", act);
    }

    [Fact]
    public void Constructor_WhenMetricsIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(_memoryCache, _redis, _publisher, null!, _options, _logger);
        Assert.Throws<ArgumentNullException>("metrics", act);
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(_memoryCache, _redis, _publisher, _metrics, null!, _logger);
        Assert.Throws<ArgumentNullException>("options", act);
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ShouldThrow()
    {
        var act = () => new HybridCache(_memoryCache, _redis, _publisher, _metrics, _options, null!);
        Assert.Throws<ArgumentNullException>("logger", act);
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldSucceed()
    {
        var cache = new HybridCache(_memoryCache, _redis, _publisher, _metrics, _options, _logger);
        Assert.NotNull(cache);
    }
}
