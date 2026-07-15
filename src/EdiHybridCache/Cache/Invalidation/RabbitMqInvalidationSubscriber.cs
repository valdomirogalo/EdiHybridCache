using System.Diagnostics.CodeAnalysis;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace EdiHybridCache.Cache.Invalidation;

[ExcludeFromCodeCoverage]
public class RabbitMqInvalidationSubscriber : ICacheInvalidationSubscriber
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HybridCacheOptions _options;
    private readonly ILogger<RabbitMqInvalidationSubscriber> _logger;
    private readonly string _queueName;

    // CWE-754 + CWE-295: async lazy init in StartAsync.
    // No .GetAwaiter().GetResult() in constructor — no thread blocking.
    private IChannel? _channel;
    private IConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    private Task? _initTask;

    public RabbitMqInvalidationSubscriber(
        IServiceProvider serviceProvider,
        IOptions<HybridCacheOptions> options,
        ILogger<RabbitMqInvalidationSubscriber> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;

        _queueName = string.IsNullOrEmpty(_options.InvalidationQueueName)
            ? $"edi.cache.invalidation.{Environment.MachineName}.{Environment.ProcessId}.{Guid.NewGuid():N}"
            : _options.InvalidationQueueName;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body;
                var message = JsonSerializer.Deserialize<InvalidationMessage>(Encoding.UTF8.GetString(body.Span));
                if (message != null)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var hybridCache = scope.ServiceProvider.GetRequiredService<IHybridCache>();
                    if (hybridCache is HybridCache hc)
                    {
                        hc.InvalidateLocal(message.Key);
                        _logger.LogDebug("Invalidated local cache for key: {Key} from remote event.", message.Key);
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
        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(
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

        await _channel.QueueDeclareAsync(
            _queueName,
            durable: true,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _channel.QueueBindAsync(
            _queueName,
            _options.InvalidationExchange,
            routingKey: "",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private ConnectionFactory CreateConnectionFactory()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.RabbitMqHost,
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
