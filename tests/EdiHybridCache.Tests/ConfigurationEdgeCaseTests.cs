using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using EdiHybridCache.Cache;
using EdiHybridCache.Configuration;

namespace EdiHybridCache.Tests;

[Collection("ConfigurationTests")]
public class ConfigurationEdgeCaseTests
{
    [Fact]
    public void PostConfigure_WithInvalidEnvVarInt_ShouldKeepDefault()
    {
        try
        {
            Environment.SetEnvironmentVariable("L1_TTL_SECONDS", "not-a-number");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddEdiHybridCache(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

            // Invalid env var should be ignored; default should remain
            options.L1TtlSeconds.Should().Be(Constants.DefaultL1TtlSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("L1_TTL_SECONDS", null);
        }
    }

    [Fact]
    public void PostConfigure_WithInvalidEnvVarDouble_ShouldKeepDefault()
    {
        // Clear any stale value from parallel tests first
        Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", null);
        try
        {
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", "not-a-double");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddEdiHybridCache(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

            options.L2TtlMultiplier.Should().Be(Constants.DefaultL2TtlMultiplier);
        }
        finally
        {
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", null);
        }
    }

    [Fact]
    public void PostConfigure_WithDoubleWithThousandsSeparator_ShouldParseAsInteger()
    {
        try
        {
            // AllowThousands treats comma as a thousands separator, so "2,5" → 25
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", "2,5");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddEdiHybridCache(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

            // With AllowThousands, "2,5" parses as 25.0 (comma is thousands separator)
            options.L2TtlMultiplier.Should().Be(25.0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("L2_TTL_MULTIPLIER", null);
        }
    }

    [Fact]
    public void PostConfigure_WithEmptyConfig_ShouldUseDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EdiHybridCache:RedisConnectionString"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEdiHybridCache(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

        options.L1TtlSeconds.Should().Be(Constants.DefaultL1TtlSeconds);
        options.DefaultL2TtlSeconds.Should().Be(Constants.DefaultL2TtlSeconds);
        options.L2TtlMultiplier.Should().Be(Constants.DefaultL2TtlMultiplier);
        options.MaxCacheSizeBytes.Should().Be(Constants.DefaultMaxCacheSizeBytes);
        options.EnableCompression.Should().BeTrue();
        options.CompressionThresholdBytes.Should().Be(Constants.DefaultCompressionThresholdBytes);
        options.RetryCount.Should().Be(Constants.DefaultRetryCount);
        options.RetryBaseDelaySeconds.Should().Be(Constants.DefaultRetryBaseDelaySeconds);
    }

    [Fact]
    public void EnvVar_EmptyString_ShouldNotOverride()
    {
        try
        {
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", "");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EdiHybridCache:RedisConnectionString"] = "from-config:6379"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddEdiHybridCache(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

            // Empty env var should not override the config value
            options.RedisConnectionString.Should().Be("from-config:6379");
        }
        finally
        {
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", null);
        }
    }
}
