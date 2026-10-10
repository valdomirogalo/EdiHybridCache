using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;
using EdiHybridCache.Configuration;

namespace EdiHybridCache.Tests;

[Collection("ConfigurationTests")]
public class ConfigurationTests
{
    [Fact]
    public void AddEdiHybridCache_WithSection_ConfiguresOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:L1TtlSeconds"] = "120",
                ["EdiHybridCache:DefaultL2TtlSeconds"] = "600",
                ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

        Assert.Equal(120, options.L1TtlSeconds);
        Assert.Equal(600, options.DefaultL2TtlSeconds);
    }

    [Fact]
    public void AddEdiHybridCache_WithConfigureAction_AppliesOverrides()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config, opts =>
        {
            opts.L1TtlSeconds = 999;
            opts.EnableCompression = false;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

        Assert.Equal(999, options.L1TtlSeconds);
        Assert.False(options.EnableCompression);
    }

    [Fact]
    public void PostConfigure_WithEnvironmentVariables_OverridesOptions()
    {
        // Arrange
        try
        {
            Environment.SetEnvironmentVariable("L1_TTL_SECONDS", "500");
            Environment.SetEnvironmentVariable("DEFAULT_L2_TTL_SECONDS", "2000");
            // Use invariant decimal separator (period) — code parses with InvariantCulture
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", "3.0");
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", "redis-prod:6379");
            Environment.SetEnvironmentVariable("INVALIDATION_CHANNEL", "edi.cache.invalidation.test");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EdiHybridCache:RedisConnectionString"] = "localhost:6379",
                    ["EdiHybridCache:L1TtlSeconds"] = "100"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddEdiHybridCache(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

            // Env vars should override config values
            Assert.Equal(500, options.L1TtlSeconds);
            Assert.Equal(2000, options.DefaultL2TtlSeconds);
            Assert.Equal(3.0, options.L2TtlMultiplier);
            Assert.Equal("redis-prod:6379", options.RedisConnectionString);
            Assert.Equal("edi.cache.invalidation.test", options.InvalidationChannel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("L1_TTL_SECONDS", null);
            Environment.SetEnvironmentVariable("DEFAULT_L2_TTL_SECONDS", null);
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", null);
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", null);
            Environment.SetEnvironmentVariable("INVALIDATION_CHANNEL", null);
        }
    }

    [Fact]
    public void AddEdiHybridCache_WithoutRedisConnection_ThrowsOnResolve()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:RedisConnectionString"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Redis connection string is not configured.", ex.Message);
    }

    [Fact]
    public async Task UseEdiHybridCacheSubscriberAsync_ShouldStartSubscriber()
    {
        var subscriberMock = new Mock<ICacheInvalidationSubscriber>();
        subscriberMock.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(subscriberMock.Object);

        var provider = services.BuildServiceProvider();
        await provider.UseEdiHybridCacheSubscriberAsync();

        subscriberMock.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AddEdiHybridCache_WithMaxCacheSize_ShouldConfigureMemoryCacheOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:RedisConnectionString"] = "localhost:6379",
                ["EdiHybridCache:MaxCacheSizeBytes"] = "2048"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MemoryCacheOptions>();

        Assert.Equal(2048L, options.SizeLimit);
        Assert.Equal(Constants.CacheCompactionPercentage, options.CompactionPercentage);
    }

    [Fact]
    public void AddEdiHybridCache_WithoutMaxCacheSize_ShouldDisableSizeLimit()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:RedisConnectionString"] = "localhost:6379",
                ["EdiHybridCache:MaxCacheSizeBytes"] = "0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MemoryCacheOptions>();

        Assert.Null(options.SizeLimit);
    }
}
