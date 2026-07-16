using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    protected HybridCache Cache { get; }

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

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<HybridCacheOptions>>(new OptionsWrapper<HybridCacheOptions>(Options));
        services.AddMemoryCache();
        services.AddSingleton(RedisMock.Object);
        services.AddSingleton(PublisherMock.Object);
        services.AddSingleton<ILogger<HybridCache>>(new NullLogger<HybridCache>());
        services.AddSingleton<CacheMetrics>();
        // Singleton: matches the library's DI registration (AddSingleton<IHybridCache, HybridCache>).
        // The static AsyncLock in HybridCache ensures cross-request stampede protection regardless.
        services.AddSingleton<HybridCache>();

        var provider = services.BuildServiceProvider();
        Cache = provider.GetRequiredService<HybridCache>();
    }
}
