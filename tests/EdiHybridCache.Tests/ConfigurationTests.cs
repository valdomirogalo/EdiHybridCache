using FluentAssertions;
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

        options.L1TtlSeconds.Should().Be(120);
        options.DefaultL2TtlSeconds.Should().Be(600);
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

        options.L1TtlSeconds.Should().Be(999);
        options.EnableCompression.Should().BeFalse();
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
            Environment.SetEnvironmentVariable("RABBITMQ_HOST", "rabbit-test");
            Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", "admin");
            Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "secret");

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
            options.L1TtlSeconds.Should().Be(500);
            options.DefaultL2TtlSeconds.Should().Be(2000);
            options.L2TtlMultiplier.Should().Be(3.0);
            options.RedisConnectionString.Should().Be("redis-prod:6379");
            options.RabbitMqHost.Should().Be("rabbit-test");
            options.RabbitMqUsername.Should().Be("admin");
            options.RabbitMqPassword.Should().Be("secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable("L1_TTL_SECONDS", null);
            Environment.SetEnvironmentVariable("DEFAULT_L2_TTL_SECONDS", null);
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", null);
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", null);
            Environment.SetEnvironmentVariable("RABBITMQ_HOST", null);
            Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", null);
            Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", null);
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

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Redis connection string is not configured.");
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
}
