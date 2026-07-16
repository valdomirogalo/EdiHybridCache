using FluentAssertions;
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

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Test");

        var cached = await Cache.GetAsync<TestClass>(key);
        cached.Should().NotBeNull();
        cached!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenL1Hit_ShouldReturnDirectly()
    {
        var key = "l1-hit-key";
        var value = new TestClass { Id = 10, Name = "L1Hit" };
        await Cache.SetAsync(key, value);

        var first = await Cache.GetAsync<TestClass>(key);
        first.Should().NotBeNull();
        first!.Id.Should().Be(10);

        RedisDbMock.Invocations.Clear();

        var second = await Cache.GetAsync<TestClass>(key);
        second.Should().NotBeNull();
        second!.Id.Should().Be(10);

        RedisDbMock.Verify(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WhenL1MissL2Miss_ShouldReturnNull()
    {
        var key = "missing-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ReturnsAsync(RedisValue.Null);

        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenRedisThrows_ShouldReturnNull()
    {
        var key = "redis-error-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ThrowsAsync(new RedisException("Connection failed"));

        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenRedisTimesOut_ShouldReturnNull()
    {
        var key = "redis-timeout-key";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                   .ThrowsAsync(new TimeoutException("Timeout"));

        var result = await Cache.GetAsync<TestClass>(key);
        result.Should().BeNull();
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
        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Name.Should().Be("Compressed");
    }

    [Fact]
    public async Task GetAsync_WhenKeyIsNull_ShouldThrow()
    {
        Func<Task> act = async () => await Cache.GetAsync<TestClass>(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_WhenKeyIsTooLong_ShouldThrow()
    {
        var longKey = new string('a', 513);
        Func<Task> act = async () => await Cache.GetAsync<TestClass>(longKey);
        await act.Should().ThrowAsync<ArgumentException>();
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
        cached.Should().NotBeNull();
        cached!.Id.Should().Be(2);
        cached.Name.Should().Be("Set");
    }

    [Fact]
    public async Task SetAsync_WhenValueIsNull_ShouldThrow()
    {
        var key = "null-key";
        Func<Task> act = () => Cache.SetAsync<TestClass>(key, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetAsync_WhenKeyIsNull_ShouldThrow()
    {
        Func<Task> act = () => Cache.SetAsync<TestClass>(null!, new TestClass());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetAsync_WhenKeyIsTooLong_ShouldThrow()
    {
        var longKey = new string('a', 513);
        Func<Task> act = () => Cache.SetAsync(longKey, new TestClass());
        await act.Should().ThrowAsync<ArgumentException>();
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
        cached.Should().NotBeNull();
        cached!.Id.Should().Be(7);
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
        cached.Should().NotBeNull();
        cached!.Id.Should().Be(9);
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
        cached.Should().BeNull();
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
        await act.Should().ThrowAsync<RedisException>();

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
        resultA.Should().NotBeNull();
        resultA!.Id.Should().Be(42);

        var resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5));
        resultB.Should().NotBeNull();
        resultB!.Id.Should().Be(42);

        RedisDbMock.Verify(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()), Times.Never);
    }

    private class TestClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
