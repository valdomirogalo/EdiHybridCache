using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Tests;

public class RedisInvalidationPublisherTests
{
    [Fact]
    public async Task PublishInvalidationAsync_ShouldPublishUtf8PayloadToConfiguredChannel()
    {
        // Arrange
        var options = new HybridCacheOptions { InvalidationChannel = "test.channel" };
        RedisChannel capturedChannel = default;
        RedisValue capturedValue = default;

        var subscriberMock = new Mock<ISubscriber>();
        subscriberMock
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((channel, value, _) =>
            {
                capturedChannel = channel;
                capturedValue = value;
            })
            .ReturnsAsync(1L);

        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(subscriberMock.Object);

        var publisher = new RedisInvalidationPublisher(
            redisMock.Object,
            new OptionsWrapper<HybridCacheOptions>(options),
            NullLogger<RedisInvalidationPublisher>.Instance);

        // Act
        await publisher.PublishInvalidationAsync("my-key");

        // Assert
        Assert.Equal("test.channel", capturedChannel.ToString());

        // Payload is published as raw UTF-8 bytes (single allocation, no intermediate string)
        var bytes = (byte[]?)capturedValue;
        Assert.NotNull(bytes);
        using var doc = JsonDocument.Parse(bytes!);
        Assert.Equal("my-key", doc.RootElement.GetProperty("Key").GetString());
        Assert.True(doc.RootElement.TryGetProperty("Timestamp", out _));

        subscriberMock.Verify(
            s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishInvalidationAsync_WhenKeyIsNull_ShouldThrow()
    {
        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(x => x.GetSubscriber(It.IsAny<object>())).Returns(new Mock<ISubscriber>().Object);

        var publisher = new RedisInvalidationPublisher(
            redisMock.Object,
            new OptionsWrapper<HybridCacheOptions>(new HybridCacheOptions()),
            NullLogger<RedisInvalidationPublisher>.Instance);

        var act = async () => await publisher.PublishInvalidationAsync(null!);

        await Assert.ThrowsAsync<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenRedisConnectionIsNull_ShouldThrow()
    {
        var options = new OptionsWrapper<HybridCacheOptions>(new HybridCacheOptions());
        var act = () => new RedisInvalidationPublisher(null!, options, NullLogger<RedisInvalidationPublisher>.Instance);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ShouldThrow()
    {
        var act = () => new RedisInvalidationPublisher(new Mock<IConnectionMultiplexer>().Object, null!, NullLogger<RedisInvalidationPublisher>.Instance);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ShouldThrow()
    {
        var options = new OptionsWrapper<HybridCacheOptions>(new HybridCacheOptions());
        var act = () => new RedisInvalidationPublisher(new Mock<IConnectionMultiplexer>().Object, options, null!);
        Assert.Throws<ArgumentNullException>(act);
    }
}
