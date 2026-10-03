using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EdiHybridCache.Cache.Invalidation;

public class RedisInvalidationSubscriber : ICacheInvalidationSubscriber
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RedisInvalidationSubscriber> _logger;
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;
    private readonly Action<RedisChannel, RedisValue> _handler;

    private bool _disposed;

    public RedisInvalidationSubscriber(
        IServiceProvider serviceProvider,
        IConnectionMultiplexer redisConnection,
        IOptions<HybridCacheOptions> options,
        ILogger<RedisInvalidationSubscriber> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(redisConnection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _subscriber = redisConnection.GetSubscriber();
        _channel = RedisChannel.Literal(options.Value.InvalidationChannel);
        _handler = OnMessage;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _subscriber.SubscribeAsync(_channel, _handler).ConfigureAwait(false);
        _logger.LogInformation("Started invalidation subscriber on Redis channel: {Channel}", _channel);
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _subscriber.Unsubscribe(_channel, _handler);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void OnMessage(RedisChannel channel, RedisValue value)
    {
        try
        {
            // Received values are backed by the raw bytes, so the cast does not copy.
            var message = JsonSerializer.Deserialize<InvalidationMessage>((byte[]?)value);
            if (message != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var hybridCache = scope.ServiceProvider.GetRequiredService<IHybridCache>();
                if (hybridCache is HybridCache hc)
                {
                    hc.InvalidateLocal(message.Key);
                    _logger.LogDebug("Invalidated local cache for key: {Key} from remote event.", Constants.SanitizeForLog(message.Key));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing invalidation message.");
        }
    }

    private class InvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }
}
