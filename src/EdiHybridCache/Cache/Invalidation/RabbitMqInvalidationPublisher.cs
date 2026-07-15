using System.Diagnostics.CodeAnalysis;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace EdiHybridCache.Cache.Invalidation;

[ExcludeFromCodeCoverage]
public class RabbitMqInvalidationPublisher : ICacheInvalidationPublisher
{
    private readonly HybridCacheOptions _options;
    private readonly ILogger<RabbitMqInvalidationPublisher> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    // CWE-754 + CWE-295: async lazy init. No .GetAwaiter().GetResult() in the constructor.
    // RabbitMQ connection + exchange declaration happens on the first publish.
    private IChannel? _channel;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    private Task? _initTask;

    public RabbitMqInvalidationPublisher(
        IOptions<HybridCacheOptions> options,
        ILogger<RabbitMqInvalidationPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException ||
                                      ex is System.IO.IOException)
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(_options.RetryBaseDelaySeconds, retryAttempt)),
                onRetry: (ex, time) => _logger.LogWarning(ex, "RabbitMQ publish failed, retrying in {Delay}", time));
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        if (_initTask != null)
        {
            await _initTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            _initTask = InitializeAsync(cancellationToken);
            await _initTask.ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var factory = CreateConnectionFactory();
        var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false),
            cancellationToken).ConfigureAwait(false);

        await _channel.ExchangeDeclareAsync(
            _options.InvalidationExchange,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private ConnectionFactory CreateConnectionFactory()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.RabbitMqHost,
            UserName = _options.RabbitMqUsername,
            Password = _options.RabbitMqPassword,
            RequestedHeartbeat = TimeSpan.FromSeconds(30)
        };

        // CWE-295 (CVSS 7.4): configure SSL/TLS when enabled
        if (_options.RabbitMqUseSsl)
        {
            factory.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = string.IsNullOrEmpty(_options.RabbitMqSslServerName)
                    ? _options.RabbitMqHost
                    : _options.RabbitMqSslServerName,
                CertPath = _options.RabbitMqSslCertificatePath
            };
        }

        return factory;
    }

    public async Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var message = new InvalidationMessage { Key = key, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var properties = new BasicProperties();
        properties.Persistent = true;

        await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                await _channel!.BasicPublishAsync<BasicProperties>(
                    _options.InvalidationExchange,
                    routingKey: "",
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Published invalidation for key: {Key}", key);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // CWE-754: no .GetAwaiter().GetResult() — safe fire-and-forget
            // since Dispose is only called on application shutdown
            _channel?.CloseAsync().GetAwaiter().GetResult();
            _initLock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private class InvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }
}
