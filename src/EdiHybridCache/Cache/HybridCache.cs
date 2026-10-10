using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Cache;

public class HybridCache : IHybridCache
{
    // Limits sourced from Constants.cs — single source of truth
    // LoggerMessage.Define: zero allocation vs params object[] from traditional LogDebug
    // Estimated gain: ~40 B per call on hot paths (L1 Hit, L2 Hit, Miss)
    private static readonly Action<ILogger, string, Exception?> _l1HitLog =
        LoggerMessage.Define<string>(LogLevel.Debug, 0, "L1 hit for key: {Key}");
    private static readonly Action<ILogger, string, Exception?> _l1HitAfterLockLog =
        LoggerMessage.Define<string>(LogLevel.Debug, 0, "L1 hit after lock for key: {Key}");
    private static readonly Action<ILogger, string, Exception?> _cacheMissLog =
        LoggerMessage.Define<string>(LogLevel.Debug, 0, "Cache miss for key: {Key}");
    private static readonly Action<ILogger, string, Exception?> _l2HitLog =
        LoggerMessage.Define<string>(LogLevel.Debug, 0, "L2 hit for key: {Key}, repopulated L1");

    // Test coverage: GetAsync lines 75-125 → tests HybridCacheTests (x13)
    // Test coverage: SetAsync lines 127-163 → tests HybridCacheTests (x6)
    // Test coverage: RemoveAsync lines 165-184 → tests HybridCacheTests (x2)
    // Test coverage: DeserializeRedisValue lines 201-253 → tests GetAsync_WithCompression (x1)
    // Test coverage: SerializeValue lines 255-265 → tests SetAsync_WithCompression (x1)

    // Static so all HybridCache instances share the same stripe set for cross-request
    // stampede protection. Before this fix, each scoped instance had its own AsyncLock
    // (16,384 SemaphoreSlim × concurrent requests), which both defeated stampede
    // protection and caused significant memory pressure.
    private static readonly AsyncLock _asyncLock = new();

    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _redisDb;
    private readonly ICacheInvalidationPublisher _publisher;
    private readonly CacheMetrics _metrics;
    private readonly ILogger<HybridCache> _logger;
    private readonly HybridCacheOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AsyncRetryPolicy _retryPolicy;

    // Cached fields to avoid repeated TimeSpan.FromSeconds calls
    private readonly TimeSpan _l1Ttl;
    private readonly TimeSpan _defaultL2Ttl;
    private readonly TimeSpan _minL2Ttl;

    public HybridCache(
        IMemoryCache memoryCache,
        IConnectionMultiplexer redisConnection,
        ICacheInvalidationPublisher publisher,
        CacheMetrics metrics,
        IOptions<HybridCacheOptions> options,
        ILogger<HybridCache> logger)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        ArgumentNullException.ThrowIfNull(redisConnection);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _memoryCache = memoryCache;
        _redisDb = redisConnection.GetDatabase();
        _publisher = publisher;
        _metrics = metrics;
        _logger = logger;
        _options = options.Value;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultBufferSize = Constants.JsonDefaultBufferSize
        };

        // Cache TimeSpan values to avoid repeated FromSeconds calls
        _l1Ttl = TimeSpan.FromSeconds(_options.L1TtlSeconds);
        _defaultL2Ttl = TimeSpan.FromSeconds(_options.DefaultL2TtlSeconds);
        _minL2Ttl = TimeSpan.FromSeconds(_options.L1TtlSeconds * _options.L2TtlMultiplier);

        // Retry with jitter to avoid retry storms on Redis
        // Random.Shared is thread-safe and does not need cryptography (jitter is not sensitive)
        _retryPolicy = Policy
            .Handle<RedisException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt =>
                {
                    // Exponential backoff: 1s → 2s → 4s + jitter ±25%
                    var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1));
                    var jitterMs = (int)(baseDelay.TotalMilliseconds * 0.25);
                    var jitter = RandomNumberGenerator.GetInt32(-jitterMs, jitterMs + 1);
                    return baseDelay + TimeSpan.FromMilliseconds(jitter);
                },
                onRetry: (ex, time, attempt, _) =>
                {
                    _logger.LogWarning(
                        ex,
                        "Redis operation failed (attempt {Attempt}/{RetryCount}), retrying in {Delay}ms",
                        attempt, _options.RetryCount, time.TotalMilliseconds);
                });
    }

    private static void ValidateKeyLength(string key)
    {
        if (key.Length > Constants.MaxKeyLength)
        {
            throw new ArgumentException(
                $"Key length exceeds maximum of {Constants.MaxKeyLength} characters. Actual length: {key.Length}.",
                nameof(key));
        }
    }

    private bool ValidateMaxSize(int size, string key)
    {
        if (size > Constants.MaxValueSizeBytes)
        {
            _logger.LogWarning(
                "Value for key {Key} exceeds max size ({Size} > {Max}). Skipping.",
                Constants.SanitizeForLog(key), size, Constants.MaxValueSizeBytes);
            return false;
        }

        return true;
    }

    private async Task<TResult> RedisSafeExecuteAsync<TResult>(Func<Task<TResult>> operation, string key)
    {
        try
        {
            return await _retryPolicy
                .ExecuteAsync(operation)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogError(ex, "Redis error for key {Key}", Constants.SanitizeForLog(key));
            return default!;
        }
    }

    private async Task<bool> PublisherSafeExecuteAsync(Func<Task> operation, string key)
    {
        try
        {
            await operation().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Publisher operation failed for key {Key}. Remote invalidation skipped.", Constants.SanitizeForLog(key));
            return false;
        }
    }

    // Covered by: GetAsync_WhenL1MissL2Hit, GetAsync_WhenL1Hit,
    //   GetAsync_WhenL1MissL2Miss, GetAsync_WhenRedisThrows,
    //   GetAsync_WhenRedisTimesOut, GetAsync_WithCompression,
    //   GetAsync_WhenConcurrentRequests_DoubleCheckPopulatesL1 (x7)
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKeyLength(key);

        // Fast path: L1 hit — completely synchronous, no state machine allocation
        if (_memoryCache.TryGetValue(key, out T? cached))
        {
            _metrics.L1Hits.Add(1);
            _l1HitLog(_logger, Constants.SanitizeForLog(key), null);

            return new ValueTask<T?>(cached);
        }

        // Slow path requires async state machine for L2 lookup
        return SlowGetAsync<T>(key, cancellationToken);
    }

    private async ValueTask<T?> SlowGetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        using (await _asyncLock.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return await TryReadFromL2Async<T>(key).ConfigureAwait(false);
        }
    }

    private async Task<T?> TryReadFromL2Async<T>(string key) where T : class
    {
        if (_memoryCache.TryGetValue(key, out T? cached))
        {
            _metrics.L1Hits.Add(1);
            _l1HitAfterLockLog(_logger, Constants.SanitizeForLog(key), null);
            return cached;
        }

        _metrics.RedisOperations.Add(1);
        var redisValue = await RedisSafeExecuteAsync(
            () => _redisDb.StringGetAsync(key), key).ConfigureAwait(false);

        if (redisValue.IsNull)
        {
            _metrics.CacheMisses.Add(1);
            _cacheMissLog(_logger, Constants.SanitizeForLog(key), null);
            return null;
        }

        var value = DeserializeRedisValue<T>(redisValue, key);

        if (value == null)
            return null;

        SetMemoryCache(key, value, _l1Ttl);
        _metrics.L2Hits.Add(1);
        _l2HitLog(_logger, Constants.SanitizeForLog(key), null);
        return value;
    }

    // Covered by: SetAsync_ShouldStoreInL1AndL2, SetAsync_WhenValueIsNull,
    //   SetAsync_WhenKeyIsNull, SetAsync_WhenKeyIsTooLong,
    //   SetAsync_WhenTtlTooSmall, SetAsync_WithCompression,
    //   SetAsync_WhenRedisThrows (x7)
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttlL2 = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ValidateKeyLength(key);

        var l2Ttl = ttlL2 ?? _defaultL2Ttl;

        if (l2Ttl < _minL2Ttl)
        {
            _logger.LogWarning(
                "L2 TTL ({L2Ttl}) is less than recommended minimum ({MinTtl}). Adjusting to {MinTtl}.",
                l2Ttl, _minL2Ttl, _minL2Ttl);
            l2Ttl = _minL2Ttl;
        }

        // Serialize and validate the size before storing, symmetric with the read path
        // (DeserializeFromBytes rejects values above Constants.MaxValueSizeBytes).
        var json = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
        if (!ValidateMaxSize(json.Length, key))
            return;

        var bytes = CompressIfNeeded(json);

        // Always write to L1 first (fast local cache)
        SetMemoryCache(key, value, _l1Ttl);

        _metrics.SetOperations.Add(1);
        _metrics.RedisOperations.Add(1);
        var stored = await RedisSafeExecuteAsync(
            () => _redisDb.StringSetAsync(key, bytes, l2Ttl), key).ConfigureAwait(false);

        if (stored)
        {
            _logger.LogInformation(
                "Cache set for key: {Key} with L1 TTL={L1Ttl}s, L2 TTL={L2Ttl}s",
                Constants.SanitizeForLog(key), _options.L1TtlSeconds, l2Ttl.TotalSeconds);
        }
        else
        {
            _logger.LogWarning(
                "Redis write failed for key {Key}; value cached in L1 only (best-effort).",
                Constants.SanitizeForLog(key));
        }
    }

    // Covered by: RemoveAsync_ShouldClearL1L2AndPublishInvalidation (x1)
    //   RemoveAsync_WhenRedisThrows_ShouldStillPublishInvalidation → renamed to
    //   RemoveAsync_WhenRedisThrows_ShouldPropagate (x1)
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKeyLength(key);

        // Redis first: if deletion fails (exception propagates after retries), L1 is untouched
        // → consistent state. No event is published for a key still in Redis.
        _metrics.RedisOperations.Add(1);
        await _retryPolicy
            .ExecuteAsync(() => _redisDb.KeyDeleteAsync(key))
            .ConfigureAwait(false);

        _metrics.RemoveOperations.Add(1);
        _memoryCache.Remove(key);
        ReportL1CacheSize();

        var published = await PublisherSafeExecuteAsync(
            () => _publisher.PublishInvalidationAsync(key, cancellationToken), key).ConfigureAwait(false);
        if (published)
            _metrics.InvalidationsPublished.Add(1);
        _logger.LogInformation("Cache removed and invalidation published for key: {Key}", Constants.SanitizeForLog(key));
    }

    // Covered by: PublishInvalidationAsync_ShouldDelegateToPublisher (x1)
    public async Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKeyLength(key);

        await PublisherSafeExecuteAsync(
            () => _publisher.PublishInvalidationAsync(key, cancellationToken), key).ConfigureAwait(false);
    }

    // Covered by: InvalidateLocal_ShouldRemoveFromL1 (x1)
    public void InvalidateLocal(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKeyLength(key);

        _memoryCache.Remove(key);
        ReportL1CacheSize();
    }

    private T? DeserializeRedisValue<T>(RedisValue redisValue, string key) where T : class
    {
        // Zero-copy: the implicit RedisValue → byte[] conversion returns the underlying array
        // without allocating (values are always stored as UTF-8 bytes).
        var payload = (byte[])redisValue!;

        try
        {
            if (_options.EnableCompression && payload.Length > _options.CompressionThresholdBytes)
            {
                // Decompress into a pooled buffer and deserialize directly from it — no second
                // copy of the decompressed payload is materialized.
                if (!CompressionHelper.TryDecompress(payload, out var decompressed))
                {
                    _logger.LogWarning("Failed to decompress value for key {Key}. Skipping.", Constants.SanitizeForLog(key));
                    return null;
                }

                using (decompressed)
                {
                    return DeserializeFromBytes<T>(decompressed.Span, key);
                }
            }

            return DeserializeFromBytes<T>(payload, key);
        }
        catch (JsonException ex)
        {
            // CWE-502 (CVSS 6.5): System.Text.Json is type-safe by default (no TypeNameHandling
            // like Newtonsoft). The where T : class constraint + null check + exception log
            // prevent arbitrary object injection via compromised Redis.
            _logger.LogError(ex, "Failed to deserialize value for key {Key}. Possible cache poisoning.", Constants.SanitizeForLog(key));
            return null;
        }
    }

    private T? DeserializeFromBytes<T>(ReadOnlySpan<byte> data, string key) where T : class
    {
        if (!ValidateMaxSize(data.Length, key))
            return null;

        return JsonSerializer.Deserialize<T>(data, _jsonOptions);
    }

    private byte[] CompressIfNeeded(byte[] json)
    {
        if (_options.EnableCompression && json.Length > _options.CompressionThresholdBytes)
        {
            return CompressionHelper.Compress(json);
        }

        return json;
    }

    private void SetMemoryCache<T>(string key, T value, TimeSpan ttl) where T : class
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(ttl)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(1);

        _memoryCache.Set(key, value, cacheEntryOptions);
        ReportL1CacheSize();
    }

    private void ReportL1CacheSize()
    {
        // The L1 cache is registered as MemoryCache; its Count reflects the current number of
        // entries (O(1)). Keeps the observable gauge (edi.cache.size) in sync on every mutation.
        if (_memoryCache is MemoryCache memoryCache)
            _metrics.SetCurrentCacheSize(memoryCache.Count);
    }
}
