using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

/// <summary>
/// Edge-case tests for HybridCache covering boundary conditions, empty inputs,
/// cancellation, deserialization failures, and silent error paths.
/// </summary>
public class HybridCacheEdgeCaseTests : TestBase
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // ─────────────────────────────────────────────────────────────
    //  GetAsync — edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenKeyIsEmpty_ShouldNotThrow()
    {
        RedisDbMock.Setup(x => x.StringGetAsync("", It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);
        var result = await Cache.GetAsync<string>("");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenKeyIsWhitespace_ShouldNotThrowValidation()
    {
        RedisDbMock.Setup(x => x.StringGetAsync("   ", It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);
        var result = await Cache.GetAsync<string>("   ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenDeserializedValueIsNull_ShouldReturnNull()
    {
        Options.EnableCompression = false;
        var key = "null-deserialized";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)"null"u8.ToArray());
        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenRedisReturnsCorruptData_ShouldReturnNull()
    {
        Options.EnableCompression = false;
        var key = "corrupt-data";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)new byte[] { 0xFF, 0xFE, 0x00, 0x01 });
        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenCompressionEnabledAndDataIsNotCompressed_ShouldReturnNull()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;
        var key = "not-compressed";
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new TestClass { Id = 1, Name = "Plain" }, CamelCaseOptions);
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)json);
        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenKeyBoundaryLength_ShouldSucceed()
    {
        var key = new string('a', Constants.MaxKeyLength);
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);
        var result = await Cache.GetAsync<string>(key);
        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────
    //  SetAsync — edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_WhenKeyIsEmpty_ShouldNotThrow()
    {
        RedisDbMock.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        await Cache.SetAsync("", "value");
        RedisDbMock.Verify(x => x.StringSetAsync(
            "", It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenTtlIsZero_ShouldUseDefault()
    {
        var key = "zero-ttl";
        await Cache.SetAsync(key, "value", TimeSpan.Zero);
        RedisDbMock.Verify(x => x.StringSetAsync(
            key, It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds >= 90)), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenTtlExactlyAtMinimum_ShouldNotAdjust()
    {
        var key = "exact-min-ttl";
        var exactMin = TimeSpan.FromSeconds(90);
        await Cache.SetAsync(key, "value", exactMin);
        RedisDbMock.Verify(x => x.StringSetAsync(
            key, It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds == 90)), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenRedisTimesOut_ShouldNotThrow()
    {
        var key = "set-timeout";
        RedisDbMock.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new TimeoutException("Redis timeout"));
        await Cache.SetAsync(key, "value");
        var cached = await Cache.GetAsync<string>(key);
        cached.Should().Be("value");
    }

    [Fact]
    public async Task SetAsync_WithCompressionBelowThreshold_ShouldNotCompress()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 10_000;
        var key = "no-compress-needed";
        var value = new string('x', 100);
        await Cache.SetAsync(key, value);
        RedisDbMock.Verify(x => x.StringSetAsync(
            key, It.Is<RedisValue>(v => ((byte[])v!).Length > 100),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WithCompressionAtThreshold_ShouldCompress()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;
        var key = "compress-at-threshold";
        var value = new TestClass { Id = 99, Name = "Threshold" };
        await Cache.SetAsync(key, value);
        RedisDbMock.Verify(x => x.StringSetAsync(
            key, It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────
    //  RemoveAsync — edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_WhenKeyIsEmpty_ShouldNotThrow()
    {
        RedisDbMock.Setup(x => x.KeyDeleteAsync("", It.IsAny<CommandFlags>()))
                   .ReturnsAsync(true);
        await Cache.RemoveAsync("");
        RedisDbMock.Verify(x => x.KeyDeleteAsync("", It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyNotInCache_ShouldNotThrow()
    {
        var key = "non-existent-key";
        RedisDbMock.Setup(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync(true);
        await Cache.RemoveAsync(key);
    }

    [Fact]
    public async Task RemoveAsync_WhenPublisherThrows_ShouldNotThrow()
    {
        var key = "publisher-throws";
        await Cache.SetAsync(key, "value");
        PublisherMock.Setup(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Publisher failed"));
        await Cache.RemoveAsync(key);
    }

    // ─────────────────────────────────────────────────────────────
    //  InvalidateLocal — edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidateLocal_WhenKeyIsNull_ShouldThrow()
    {
        var act = () => Cache.InvalidateLocal(null!);
        Assert.Throws<ArgumentNullException>("key", act);
    }

    [Fact]
    public void InvalidateLocal_WhenKeyNotInL1_ShouldNotThrow()
    {
        Cache.InvalidateLocal("not-in-l1");
    }

    // ─────────────────────────────────────────────────────────────
    //  PublishInvalidation — edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishInvalidationAsync_WhenPublisherThrows_ShouldNotThrow()
    {
        var key = "pub-fail";
        PublisherMock.Setup(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fail"));
        await Cache.PublishInvalidationAsync(key);
    }

    [Fact]
    public async Task PublishInvalidationAsync_WhenKeyIsEmpty_ShouldNotThrow()
    {
        PublisherMock.Setup(x => x.PublishInvalidationAsync("", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await Cache.PublishInvalidationAsync("");
    }

    // ─────────────────────────────────────────────────────────────
    //  Double-checked locking edge cases
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_L1MissDuringLock_L2Miss_SecondCallShouldMissAgain()
    {
        var key = "double-miss";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);
        var first = await Cache.GetAsync<string>(key);
        first.Should().BeNull();
        var second = await Cache.GetAsync<string>(key);
        second.Should().BeNull();
        RedisDbMock.Verify(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAsync_WithCompressionAndCorruptData_ShouldReturnNull()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;
        var key = "corrupt-compressed";
        var garbage = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)garbage);
        var result = await Cache.GetAsync<string>(key);
        result.Should().BeNull();
    }

    private class TestClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
