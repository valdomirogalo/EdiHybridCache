using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Tests;

public class RedisInvalidationSubscriberTests : TestBase
{
    private (RedisInvalidationSubscriber Subscriber, Mock<ISubscriber> SubscriberMock, RedisChannel Channel) CreateSubscriber(string channelName)
    {
        Options.InvalidationChannel = channelName;

        var subscriberMock = new Mock<ISubscriber>();
        RedisMock.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(subscriberMock.Object);

        var subscriber = new RedisInvalidationSubscriber(
            Provider,
            RedisMock.Object,
            new OptionsWrapper<HybridCacheOptions>(Options),
            NullLogger<RedisInvalidationSubscriber>.Instance);

        return (subscriber, subscriberMock, RedisChannel.Literal(channelName));
    }

    private static HandlerHolder CaptureHandler(Mock<ISubscriber> subscriberMock)
    {
        var holder = new HandlerHolder();
        subscriberMock
            .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((c, h, f) => holder.Handler = h)
            .Returns(Task.CompletedTask);
        return holder;
    }

    [Fact]
    public async Task StartAsync_ShouldSubscribe_AndInvalidateLocalCacheOnMessage()
    {
        var (subscriber, subscriberMock, channel) = CreateSubscriber("test.channel");
        var holder = CaptureHandler(subscriberMock);

        await Cache.SetAsync("remote-key", "value");
        var memoryCache = Provider.GetRequiredService<IMemoryCache>();
        Assert.True(memoryCache.TryGetValue("remote-key", out string? _));

        await subscriber.StartAsync();
        Assert.NotNull(holder.Handler);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Key = "remote-key", Timestamp = 1L });
        holder.Handler!(channel, payload);

        Assert.False(memoryCache.TryGetValue("remote-key", out string? _));
    }

    [Fact]
    public async Task OnMessage_WithMalformedPayload_ShouldNotThrow()
    {
        var (subscriber, subscriberMock, channel) = CreateSubscriber("test.channel");
        var holder = CaptureHandler(subscriberMock);
        await subscriber.StartAsync();

        var act = () => holder.Handler!(channel, (RedisValue)"not-json");

        Assert.Null(Record.Exception(act));
    }

    [Fact]
    public async Task Stop_ShouldUnsubscribeFromChannel()
    {
        var (subscriber, subscriberMock, _) = CreateSubscriber("test.channel");
        await subscriber.StartAsync();

        subscriber.Stop();

        subscriberMock.Verify(
            s => s.Unsubscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Dispose_ShouldUnsubscribeOnlyOnce()
    {
        var (subscriber, subscriberMock, _) = CreateSubscriber("test.channel");
        await subscriber.StartAsync();

        subscriber.Dispose();
        subscriber.Dispose();

        subscriberMock.Verify(
            s => s.Unsubscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Stop_AfterDispose_ShouldBeNoOp()
    {
        var (subscriber, subscriberMock, _) = CreateSubscriber("test.channel");
        await subscriber.StartAsync();

        subscriber.Dispose();
        subscriber.Stop();

        subscriberMock.Verify(
            s => s.Unsubscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public void RedisValue_FromUtf8Bytes_ShouldNotCopyOnCast()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Key = "remote-key", Timestamp = 1L });

        RedisValue value = bytes;
        var roundtrip = (byte[]?)value;

        Assert.True(ReferenceEquals(bytes, roundtrip));
    }

    [Fact]
    public async Task PublisherToSubscriber_ShouldInvalidateFromRawUtf8Bytes()
    {
        Options.InvalidationChannel = "roundtrip.channel";

        RedisValue captured = default;
        Action<RedisChannel, RedisValue>? handler = null;
        var busMock = new Mock<ISubscriber>();
        busMock
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((channel, value, flags) => captured = value)
            .ReturnsAsync(1L);
        busMock
            .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((channel, h, flags) => handler = h)
            .Returns(Task.CompletedTask);
        RedisMock.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(busMock.Object);

        var publisher = new RedisInvalidationPublisher(
            RedisMock.Object, new OptionsWrapper<HybridCacheOptions>(Options), NullLogger<RedisInvalidationPublisher>.Instance);
        var subscriber = new RedisInvalidationSubscriber(
            Provider, RedisMock.Object, new OptionsWrapper<HybridCacheOptions>(Options), NullLogger<RedisInvalidationSubscriber>.Instance);

        await Cache.SetAsync("roundtrip-key", "value");
        await subscriber.StartAsync();

        // The publisher emits raw UTF-8 bytes...
        await publisher.PublishInvalidationAsync("roundtrip-key");
        Assert.NotNull((byte[]?)captured);

        // ...and the subscriber consumes those exact bytes (no string round-trip).
        handler!(RedisChannel.Literal("roundtrip.channel"), captured);

        Assert.False(Provider.GetRequiredService<IMemoryCache>().TryGetValue("roundtrip-key", out string? _));
    }

    [Fact]
    public void Constructor_WhenServiceProviderIsNull_ShouldThrow()
    {
        var act = () => new RedisInvalidationSubscriber(null!, RedisMock.Object, new OptionsWrapper<HybridCacheOptions>(Options), NullLogger<RedisInvalidationSubscriber>.Instance);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenRedisConnectionIsNull_ShouldThrow()
    {
        var act = () => new RedisInvalidationSubscriber(Provider, null!, new OptionsWrapper<HybridCacheOptions>(Options), NullLogger<RedisInvalidationSubscriber>.Instance);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ShouldThrow()
    {
        var act = () => new RedisInvalidationSubscriber(Provider, RedisMock.Object, null!, NullLogger<RedisInvalidationSubscriber>.Instance);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ShouldThrow()
    {
        var act = () => new RedisInvalidationSubscriber(Provider, RedisMock.Object, new OptionsWrapper<HybridCacheOptions>(Options), null!);
        Assert.Throws<ArgumentNullException>(act);
    }

    private sealed class HandlerHolder
    {
        public Action<RedisChannel, RedisValue>? Handler { get; set; }
    }
}
