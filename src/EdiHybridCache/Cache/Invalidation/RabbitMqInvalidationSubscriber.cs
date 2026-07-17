using System.Diagnostics.CodeAnalysis;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace EdiHybridCache.Cache.Invalidation;

[ExcludeFromCodeCoverage]
public class RabbitMqInvalidationSubscriber : ICacheInvalidationSubscriber
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HybridCacheOptions _options;
    private readonly ILogger<RabbitMqInvalidationSubscriber> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly string _queueName;

    private IChannel? _channel;
    private IConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public RabbitMqInvalidationSubscriber(
        IServiceProvider serviceProvider,
        IOptions<HybridCacheOptions> options,
        ILogger<RabbitMqInvalidationSubscriber> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;

        _queueName = string.IsNullOrEmpty(_options.InvalidationQueueName)
            ? $"{Constants.DefaultInvalidationExchange}.{Environment.MachineName}.{Environment.ProcessId}.{Guid.NewGuid():N}"
            : _options.InvalidationQueueName;

        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException ||
                                      ex is System.IO.IOException)
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(_options.RetryBaseDelaySeconds, retryAttempt)),
                onRetry: (ex, time) => _logger.LogWarning(ex, "RabbitMQ connection failed, retrying in {Delay}...", time));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await StartConsumerAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            // Schedule background retry only if startup failed
            if (!_initialized)
            {
                _ = RetryInitializeForeverAsync();
            }
        }
    }

    private async Task StartConsumerAsync(CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body;

                // Deserialize directly from span, avoiding intermediate string allocation
                var message = JsonSerializer.Deserialize<InvalidationMessage>(body.Span);
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
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invalidation message.");
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
            }
        };

        await _channel!.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
        _logger.LogInformation("Started invalidation subscriber on queue: {QueueName}", _queueName);
    }

    private async Task RetryInitializeForeverAsync()
    {
        const int maxDelaySeconds = 60;
        var delay = _options.RetryBaseDelaySeconds;

        while (!_disposed)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);

                if (_initialized || _disposed)
                    return;

                await _initLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_initialized || _disposed)
                        return;

                    await InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    _initialized = true;

                    await StartConsumerAsync(CancellationToken.None).ConfigureAwait(false);
                    _logger.LogInformation("RabbitMQ subscriber reconnected and started successfully.");
                    return;
                }
                finally
                {
                    _initLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ subscriber retry failed. Next attempt in {Delay}s...", delay);
                delay = Math.Min(delay * 2, maxDelaySeconds);
            }
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                var factory = CreateConnectionFactory();
                _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
                _channel = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: false,
                        publisherConfirmationTrackingEnabled: false),
                    ct).ConfigureAwait(false);

                await _channel.ExchangeDeclareAsync(
                    _options.InvalidationExchange,
                    ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct).ConfigureAwait(false);

                await _channel.QueueDeclareAsync(
                    _queueName,
                    durable: true,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: ct).ConfigureAwait(false);

                await _channel.QueueBindAsync(
                    _queueName,
                    _options.InvalidationExchange,
                    routingKey: "",
                    cancellationToken: ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private ConnectionFactory CreateConnectionFactory()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.RabbitMqHost,
            Port = _options.RabbitMqPort,
            UserName = _options.RabbitMqUsername,
            Password = _options.RabbitMqPassword
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

    public void Stop() { }

    public void Dispose()
    {
        if (!_disposed)
        {
            _channel?.CloseAsync().GetAwaiter().GetResult();
            _connection?.CloseAsync().GetAwaiter().GetResult();
            _initLock.Dispose();
            _disposed = true;
        }
    }

    private class InvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public long Timestamp { get; set; }
    }
}
