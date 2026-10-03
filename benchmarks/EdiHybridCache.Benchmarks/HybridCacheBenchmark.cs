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
    private RedisInvalidationPublisher _redisPublisher = null!;

    // Pre-allocated keys to avoid Guid allocation in the benchmark
    private readonly string _hitKey = "hit-key";
    private string _missKey = null!;
    private string _removeKey = null!;

    // Payloads of different sizes
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

        // ── Redis Pub/Sub invalidation publisher (mocked ISubscriber) ──
        // Measures the invalidation publish path: JSON serialized once to UTF-8 bytes
        // and sent as a RedisValue (no intermediate string, no extra array copy).
        var subscriberMock = new Mock<ISubscriber>();
        subscriberMock
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0L);
        var invalidationRedisMock = new Mock<IConnectionMultiplexer>();
        invalidationRedisMock.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(subscriberMock.Object);
        _redisPublisher = new RedisInvalidationPublisher(
            invalidationRedisMock.Object,
            new OptionsWrapper<HybridCacheOptions>(options),
            NullLogger<RedisInvalidationPublisher>.Instance);
    }

    private static IHybridCache CreateCache(HybridCacheOptions options, IConnectionMultiplexer redis)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<HybridCacheOptions>>(new OptionsWrapper<HybridCacheOptions>(options));
        services.AddMemoryCache();
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddSingleton<ICacheInvalidationPublisher, NoOpPublisher>();
        services.AddSingleton<ILogger<HybridCache>>(_ => NullLogger<HybridCache>.Instance);
        services.AddSingleton<CacheMetrics>();
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
    //  SETASYNC WITH COMPRESSION
    // ═══════════════════════════════════════════

    [Benchmark(Description = "SetAsync 10KB with compression")]
    public async Task SetAsync_Medium_Compressed() =>
        await _cacheCompressed.SetAsync(NextKey(), _mediumPayload);

    // ═══════════════════════════════════════════
    //  REMOVEASYNC
    // ═══════════════════════════════════════════

    [Benchmark(Description = "RemoveAsync (L1 populated)")]
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

    // ═══════════════════════════════════════════
    //  INVALIDATION PUBLISH (Redis Pub/Sub)
    // ═══════════════════════════════════════════

    [Benchmark(Description = "PublishInvalidationAsync (Redis Pub/Sub)")]
    public async Task PublishInvalidation_RedisPubSub() =>
        await _redisPublisher.PublishInvalidationAsync("invalidation-key");

    // ═══════════════════════════════════════════
    //  INVALIDATION PAYLOAD — single vs double allocation
    // ═══════════════════════════════════════════

    private const long _timestamp = 1_700_000_000L;

    private static readonly byte[] _invalidationPayload =
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new InvalidationMessage { Key = "invalidation-key", Timestamp = _timestamp });

    // Same strategy as RedisInvalidationPublisher: serialize once to UTF-8 bytes.
    [Benchmark(Description = "Invalidation payload (single alloc)")]
    public byte[] InvalidationPayload_SingleAlloc() =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new InvalidationMessage { Key = "invalidation-key", Timestamp = _timestamp });

    // Anti-pattern kept for contrast: string round-trip allocates the string plus the bytes.
    [Benchmark(Description = "Invalidation payload (double alloc)")]
    public byte[] InvalidationPayload_DoubleAlloc() =>
        System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(
                new InvalidationMessage { Key = "invalidation-key", Timestamp = _timestamp }));

    // Same strategy as RedisInvalidationSubscriber: deserialize straight from the UTF-8 bytes.
    [Benchmark(Description = "Invalidation deserialize (single alloc)")]
    public object? InvalidationDeserialize_SingleAlloc() =>
        System.Text.Json.JsonSerializer.Deserialize<InvalidationMessage>(_invalidationPayload);

    // Anti-pattern kept for contrast: materialize a string first, then deserialize from it.
    [Benchmark(Description = "Invalidation deserialize (double alloc)")]
    public object? InvalidationDeserialize_DoubleAlloc() =>
        System.Text.Json.JsonSerializer.Deserialize<InvalidationMessage>(
            System.Text.Encoding.UTF8.GetString(_invalidationPayload));

    private const string _testValue = "benchmark-value";

    private class InvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }

    private class NoOpPublisher : ICacheInvalidationPublisher
    {
        public Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    public static void Main(string[] args) => BenchmarkRunner.Run<HybridCacheBenchmark>();
}
