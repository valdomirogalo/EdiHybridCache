using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Tests;

public abstract class TestBase
{
    protected HybridCacheOptions Options { get; }
    protected Mock<IConnectionMultiplexer> RedisMock { get; }
    protected Mock<IDatabase> RedisDbMock { get; }
    protected Mock<ICacheInvalidationPublisher> PublisherMock { get; }
    protected TestLogger<HybridCache> Logger { get; }
    protected HybridCache Cache { get; }
    protected ServiceProvider Provider { get; }

    public TestBase()
    {
        Options = new HybridCacheOptions
        {
            L1TtlSeconds = 60,
            DefaultL2TtlSeconds = 300,
            L2TtlMultiplier = 1.5,
            EnableCompression = false
        };

        RedisDbMock = new Mock<IDatabase>();
        RedisMock = new Mock<IConnectionMultiplexer>();
        RedisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(RedisDbMock.Object);

        PublisherMock = new Mock<ICacheInvalidationPublisher>();

        Logger = new TestLogger<HybridCache>();

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<HybridCacheOptions>>(new OptionsWrapper<HybridCacheOptions>(Options));
        services.AddMemoryCache();
        services.AddSingleton(RedisMock.Object);
        services.AddSingleton(PublisherMock.Object);
        services.AddSingleton<ILogger<HybridCache>>(Logger);
        services.AddSingleton<CacheMetrics>();
        // Singleton: matches the library's DI registration (AddSingleton<IHybridCache, HybridCache>).
        // The static AsyncLock in HybridCache ensures cross-request stampede protection regardless.
        services.AddSingleton<HybridCache>();
        services.AddSingleton<IHybridCache>(sp => sp.GetRequiredService<HybridCache>());

        Provider = services.BuildServiceProvider();
        Cache = Provider.GetRequiredService<HybridCache>();
    }
}
