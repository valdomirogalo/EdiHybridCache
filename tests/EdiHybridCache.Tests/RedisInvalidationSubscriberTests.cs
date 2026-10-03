using System.Text.Json;
using FluentAssertions;
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
        memoryCache.TryGetValue("remote-key", out string? _).Should().BeTrue();

        await subscriber.StartAsync();
        holder.Handler.Should().NotBeNull();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Key = "remote-key", Timestamp = 1L });
        holder.Handler!(channel, payload);

        memoryCache.TryGetValue("remote-key", out string? _).Should().BeFalse();
    }

    [Fact]
    public async Task OnMessage_WithMalformedPayload_ShouldNotThrow()
    {
        var (subscriber, subscriberMock, channel) = CreateSubscriber("test.channel");
        var holder = CaptureHandler(subscriberMock);
        await subscriber.StartAsync();

        var act = () => holder.Handler!(channel, (RedisValue)"not-json");

        act.Should().NotThrow();
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
    public void RedisValue_FromUtf8Bytes_ShouldNotCopyOnCast()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Key = "remote-key", Timestamp = 1L });

        RedisValue value = bytes;
        var roundtrip = (byte[]?)value;

        ReferenceEquals(bytes, roundtrip).Should().BeTrue();
    }

    private sealed class HandlerHolder
    {
        public Action<RedisChannel, RedisValue>? Handler { get; set; }
    }
}
