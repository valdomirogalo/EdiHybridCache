using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EdiHybridCache.Cache.Invalidation;

public class RedisInvalidationPublisher : ICacheInvalidationPublisher
{
    private readonly ILogger<RedisInvalidationPublisher> _logger;
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;

    public RedisInvalidationPublisher(
        IConnectionMultiplexer redisConnection,
        IOptions<HybridCacheOptions> options,
        ILogger<RedisInvalidationPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(redisConnection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _subscriber = redisConnection.GetSubscriber();
        _channel = RedisChannel.Literal(options.Value.InvalidationChannel);
    }

    public async Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var message = new InvalidationMessage { Key = key, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        // Serialize directly to UTF-8 bytes and publish them as a RedisValue: a single allocation
        // for the payload — no intermediate string and no extra array copy.
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);

        var receivers = await _subscriber
            .PublishAsync(_channel, payload)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Published invalidation for key: {Key} to channel {Channel} ({Receivers} receiver(s))",
            Constants.SanitizeForLog(key), _channel, receivers);
    }

    public void Dispose()
    {
        // No unmanaged resources or owned disposables: ISubscriber and IConnectionMultiplexer
        // are shared singletons owned by the DI container.
    }

    private class InvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }
}
