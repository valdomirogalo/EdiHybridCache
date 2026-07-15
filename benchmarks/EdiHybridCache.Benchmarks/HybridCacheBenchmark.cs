using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class HybridCacheBenchmark
{
    private IHybridCache _cache = null!;
    private IHybridCache _cacheCompressed = null!;
    private Mock<IDatabase> _redisDbMock = null!;
    private Mock<IDatabase> _redisDbCompressedMock = null!;

    // Pre-allocated keys to avoid Guid allocation in the benchmark
    private readonly string _hitKey = "hit-key";
    private string _missKey = null!;
    private string _removeKey = null!;

    // Payloads de diferentes tamanhos
    private readonly string _smallPayload = new('x', 100);
    private readonly string _mediumPayload = new('x', 10_000);
    private readonly string _largePayload = new('x', 200_000);

    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _counter = 0;
        _missKey = "miss-key";
        _removeKey = "remove-key";

        // ── Cache without compression ──
        var options = new HybridCacheOptions
        {
            L1TtlSeconds = 60,
            DefaultL2TtlSeconds = 300,
            L2TtlMultiplier = 1.5,
            EnableCompression = false,
            CompressionThresholdBytes = 1024
        };

        _redisDbMock = new Mock<IDatabase>();
        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                 .Returns(_redisDbMock.Object);

        _redisDbMock
            .Setup(x => x.StringGetAsync(_hitKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_testValue));
        _redisDbMock
            .Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _redisDbMock
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _cache = CreateCache(options, redisMock.Object);

        // Populates L1 for GetAsync_Hit
        _cache.SetAsync(_hitKey, _testValue).GetAwaiter().GetResult();
        _cache.SetAsync(_removeKey, _testValue).GetAwaiter().GetResult();

        // ── Cache with compression ──
        var compressedOptions = new HybridCacheOptions
        {
            L1TtlSeconds = 60,
            DefaultL2TtlSeconds = 300,
            L2TtlMultiplier = 1.5,
            EnableCompression = true,
            CompressionThresholdBytes = 1
        };

        _redisDbCompressedMock = new Mock<IDatabase>();
        var redisMock2 = new Mock<IConnectionMultiplexer>();
        redisMock2.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                  .Returns(_redisDbCompressedMock.Object);

        _redisDbCompressedMock
            .Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _cacheCompressed = CreateCache(compressedOptions, redisMock2.Object);
    }

    private static IHybridCache CreateCache(HybridCacheOptions options, IConnectionMultiplexer redis)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<HybridCacheOptions>>(new OptionsWrapper<HybridCacheOptions>(options));
        services.AddMemoryCache();
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddSingleton<ICacheInvalidationPublisher, NoOpPublisher>();
        services.AddSingleton<ILogger<HybridCache>>(_ => NullLogger<HybridCache>.Instance);
        services.AddScoped<IHybridCache, HybridCache>();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHybridCache>();
    }

    private string NextKey() => $"k-{Interlocked.Increment(ref _counter)}";

    // ═══════════════════════════════════════════
    //  GETASYNC
    // ═══════════════════════════════════════════

    [Benchmark(Description = "GetAsync L1 Hit")]
    public async Task<string?> GetAsync_L1Hit() =>
        await _cache.GetAsync<string>(_hitKey);

    [Benchmark(Description = "GetAsync L2 Hit")]
    public async Task<string?> GetAsync_L2Hit()
    {
        var key = _missKey;
        _redisDbMock
            .Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_testValue));
        return await _cache.GetAsync<string>(key);
    }

    [Benchmark(Description = "GetAsync L2 Miss")]
    public async Task<string?> GetAsync_Miss()
    {
        var key = _missKey;
        _redisDbMock
            .Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        return await _cache.GetAsync<string>(key);
    }

    // ═══════════════════════════════════════════
    //  SETASYNC
    // ═══════════════════════════════════════════

    [Benchmark(Description = "SetAsync 100B")]
    public async Task SetAsync_Small() =>
        await _cache.SetAsync(NextKey(), _smallPayload);

    [Benchmark(Description = "SetAsync 10KB")]
    public async Task SetAsync_Medium() =>
        await _cache.SetAsync(NextKey(), _mediumPayload);

    [Benchmark(Description = "SetAsync 200KB (LOH)")]
    public async Task SetAsync_Large() =>
        await _cache.SetAsync(NextKey(), _largePayload);

    // ═══════════════════════════════════════════
    //  SETASYNC C/ COMPRESSÃO
    // ═══════════════════════════════════════════

    [Benchmark(Description = "SetAsync 10KB c/ compressão")]
    public async Task SetAsync_Medium_Compressed() =>
        await _cacheCompressed.SetAsync(NextKey(), _mediumPayload);

    // ═══════════════════════════════════════════
    //  REMOVEASYNC
    // ═══════════════════════════════════════════

    [Benchmark(Description = "RemoveAsync (L1 populado)")]
    public async Task RemoveAsync()
    {
        var key = _removeKey;
        await _cache.RemoveAsync(key);
        // Re-populates L1 for the next execution
        await _cache.SetAsync(key, _testValue);
    }

    // ═══════════════════════════════════════════
    //  INVALIDATELOCAL
    // ═══════════════════════════════════════════

    [Benchmark(Description = "InvalidateLocal")]
    public void InvalidateLocal()
    {
        var hc = (HybridCache)_cache;
        var key = _hitKey;
        hc.InvalidateLocal(key);
        // Re-populates for the next execution
        hc.SetAsync(key, _testValue).GetAwaiter().GetResult();
    }

    private const string _testValue = "benchmark-value";

    private class NoOpPublisher : ICacheInvalidationPublisher
    {
        public Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    public static void Main(string[] args) => BenchmarkRunner.Run<HybridCacheBenchmark>();
}
