using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class HybridCacheTests : TestBase
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetAsync_WhenL1MissL2Hit_ShouldPopulateL1AndReturnValue()
    {
        var key = "test-key";
        var value = new TestClass { Id = 1, Name = "Test" };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, CamelCaseOptions);
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)json);

        var result = await Cache.GetAsync<TestClass>(key);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.Equal("Test", result.Name);

        var cached = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(cached);
        Assert.Equal(1, cached!.Id);
    }

    [Fact]
    public async Task GetAsync_WhenL1Hit_ShouldReturnDirectly()
    {
        var key = "l1-hit-key";
        var value = new TestClass { Id = 10, Name = "L1Hit" };
        await Cache.SetAsync(key, value);

        var first = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(first);
        Assert.Equal(10, first!.Id);

        RedisDbMock.Invocations.Clear();

        var second = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(second);
        Assert.Equal(10, second!.Id);

        RedisDbMock.Verify(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WhenL1MissL2Miss_ShouldReturnNull()
    {
        var key = "missing-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);

        var result = await Cache.GetAsync<TestClass>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenRedisThrows_ShouldReturnNull()
    {
        var key = "redis-error-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ThrowsAsync(new RedisException("Connection failed"));

        var result = await Cache.GetAsync<TestClass>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenRedisTimesOut_ShouldReturnNull()
    {
        var key = "redis-timeout-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ThrowsAsync(new TimeoutException("Timeout"));

        var result = await Cache.GetAsync<TestClass>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithCompressionEnabled_ShouldDecompress()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;

        var key = "compress-key";
        var value = new TestClass { Id = 5, Name = "Compressed" };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, CamelCaseOptions);
        var compressed = CompressionHelper.Compress(json);

        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync((RedisValue)compressed);

        var result = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(result);
        Assert.Equal(5, result!.Id);
        Assert.Equal("Compressed", result.Name);
    }

    [Fact]
    public async Task GetAsync_WhenKeyIsNull_ShouldThrow()
    {
        Func<Task> act = async () => await Cache.GetAsync<TestClass>(null!);
        await Assert.ThrowsAsync<ArgumentNullException>(act);
    }

    [Fact]
    public async Task GetAsync_WhenKeyIsTooLong_ShouldThrow()
    {
        var longKey = new string('a', 513);
        Func<Task> act = async () => await Cache.GetAsync<TestClass>(longKey);
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task SetAsync_ShouldStoreInL1AndL2()
    {
        var key = "set-key";
        var value = new TestClass { Id = 2, Name = "Set" };
        var ttl = TimeSpan.FromSeconds(100);

        await Cache.SetAsync(key, value, ttl);

        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>()),
            Times.Once);

        var cached = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(cached);
        Assert.Equal(2, cached!.Id);
        Assert.Equal("Set", cached.Name);
    }

    [Fact]
    public async Task SetAsync_WhenValueIsNull_ShouldThrow()
    {
        var key = "null-key";
        Func<Task> act = () => Cache.SetAsync<TestClass>(key, null!);
        await Assert.ThrowsAsync<ArgumentNullException>(act);
    }

    [Fact]
    public async Task SetAsync_WhenKeyIsNull_ShouldThrow()
    {
        Func<Task> act = () => Cache.SetAsync<TestClass>(null!, new TestClass());
        await Assert.ThrowsAsync<ArgumentNullException>(act);
    }

    [Fact]
    public async Task SetAsync_WhenKeyIsTooLong_ShouldThrow()
    {
        var longKey = new string('a', 513);
        Func<Task> act = () => Cache.SetAsync(longKey, new TestClass());
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task SetAsync_WhenTtlTooSmall_ShouldAdjustToMinimum()
    {
        var key = "min-ttl-key";
        var value = new TestClass { Id = 7, Name = "MinTtl" };
        var smallTtl = TimeSpan.FromSeconds(10);

        await Cache.SetAsync(key, value, smallTtl);

        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds >= 90)),
            Times.Once);

        var cached = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(cached);
        Assert.Equal(7, cached!.Id);
    }

    [Fact]
    public async Task SetAsync_WithCompressionEnabled_ShouldCompress()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;

        var key = "set-compress-key";
        var value = new TestClass { Id = 8, Name = "SetCompressed" };
        await Cache.SetAsync(key, value);

        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenRedisThrows_ShouldNotThrow()
    {
        var key = "set-redis-error";
        var value = new TestClass { Id = 9, Name = "RedisError" };
        RedisDbMock.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new RedisException("Write failed"));

        await Cache.SetAsync(key, value);

        var cached = await Cache.GetAsync<TestClass>(key);
        Assert.NotNull(cached);
        Assert.Equal(9, cached!.Id);
    }

    [Fact]
    public async Task RemoveAsync_ShouldClearL1L2AndPublishInvalidation()
    {
        var key = "remove-key";
        await Cache.SetAsync(key, new TestClass { Id = 3 });

        await Cache.RemoveAsync(key);

        RedisDbMock.Verify(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
        PublisherMock.Verify(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        var cached = await Cache.GetAsync<TestClass>(key);
        Assert.Null(cached);
    }

    [Fact]
    public async Task RemoveAsync_WhenRedisThrows_ShouldPropagate()
    {
        var key = "remove-redis-error";
        await Cache.SetAsync(key, new TestClass { Id = 4 });

        RedisDbMock.Setup(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Delete failed"));

        Func<Task> act = () => Cache.RemoveAsync(key);

        // Exception propagates after retries are exhausted → L1 untouched, no event published
        await Assert.ThrowsAsync<RedisException>(act);

        PublisherMock.Verify(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishInvalidationAsync_ShouldDelegateToPublisher()
    {
        var key = "publish-delegate";
        await Cache.PublishInvalidationAsync(key);

        PublisherMock.Verify(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenConcurrentRequests_DoubleCheckPopulatesL1()
    {
        var key = "concurrent-key";
        var value = new TestClass { Id = 42, Name = "Concurrent" };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, CamelCaseOptions);

        var tcsEnterRedis = new TaskCompletionSource();
        var tcsReleaseRedis = new TaskCompletionSource();

        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .Returns(async () =>
            {
                tcsEnterRedis.TrySetResult();
                await tcsReleaseRedis.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return (RedisValue)json;
            });

        var taskA = Task.Run(async () => await Cache.GetAsync<TestClass>(key));
        await tcsEnterRedis.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var taskB = Task.Run(async () => await Cache.GetAsync<TestClass>(key));
        await Task.Delay(300);

        RedisDbMock.Invocations.Clear();
        tcsReleaseRedis.TrySetResult();

        var resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(resultA);
        Assert.Equal(42, resultA!.Id);

        var resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(resultB);
        Assert.Equal(42, resultB!.Id);

        RedisDbMock.Verify(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_WithCompression_ShouldStoreGzipBytes()
    {
        Options.EnableCompression = true;
        Options.CompressionThresholdBytes = 1;
        var key = "compress-verify";
        var value = new string('x', 10_000);

        await Cache.SetAsync(key, value);

        // The stored value must be gzip-compressed (magic header 0x1F 0x8B).
        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.Is<RedisValue>(v => ((byte[])v!)[0] == 0x1F && ((byte[])v!)[1] == 0x8B),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenCompressionDisabled_ShouldNotCompress()
    {
        Options.EnableCompression = false;
        Options.CompressionThresholdBytes = 1;
        var key = "no-compress";
        var value = new string('x', 10_000);

        await Cache.SetAsync(key, value);

        // Raw JSON starts with a double-quote (0x22), not the gzip magic (0x1F).
        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.Is<RedisValue>(v => ((byte[])v!)[0] == (byte)'"'),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_ShouldStoreCompactJson()
    {
        var key = "compact-json";
        var value = new TestClass { Id = 1, Name = "Test" };

        await Cache.SetAsync(key, value);

        RedisDbMock.Verify(x => x.StringSetAsync(
            key,
            It.Is<RedisValue>(v => !System.Text.Encoding.UTF8.GetString((byte[])v!).Contains('\n')
                                   && !System.Text.Encoding.UTF8.GetString((byte[])v!).Contains('\r')),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    private class TestClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
