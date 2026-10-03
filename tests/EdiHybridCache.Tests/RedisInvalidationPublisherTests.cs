using System.Text.Json;
using FluentAssertions;
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
        capturedChannel.ToString().Should().Be("test.channel");

        // Payload is published as raw UTF-8 bytes (single allocation, no intermediate string)
        var bytes = (byte[]?)capturedValue;
        bytes.Should().NotBeNull();
        using var doc = JsonDocument.Parse(bytes!);
        doc.RootElement.GetProperty("Key").GetString().Should().Be("my-key");
        doc.RootElement.TryGetProperty("Timestamp", out _).Should().BeTrue();

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

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
