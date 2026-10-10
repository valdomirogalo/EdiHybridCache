using Moq;
using StackExchange.Redis;
using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class LoggingTests : TestBase
{
    [Fact]
    public async Task SetAsync_ShouldLogCacheSet()
    {
        RedisDbMock.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        await Cache.SetAsync("log-set", "value");

        Assert.Contains(Logger.Messages, m => m.Contains("Cache set"));
    }

    [Fact]
    public async Task SetAsync_WhenRedisFails_ShouldLogWarning()
    {
        RedisDbMock.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new RedisException("write failed"));

        await Cache.SetAsync("log-set-fail", "value");

        Assert.Contains(Logger.Messages, m => m.Contains("Redis write failed"));
    }

    [Fact]
    public async Task RemoveAsync_ShouldLogRemoval()
    {
        RedisDbMock.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await Cache.SetAsync("log-remove", "value");
        await Cache.RemoveAsync("log-remove");

        Assert.Contains(Logger.Messages, m => m.Contains("Cache removed"));
    }

    [Fact]
    public async Task GetAsync_L2Hit_ShouldLog()
    {
        var key = "log-l2";
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes("value");
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        await Cache.GetAsync<string>(key);

        Assert.Contains(Logger.Messages, m => m.Contains("L2 hit"));
    }

    [Fact]
    public async Task GetAsync_Miss_ShouldLog()
    {
        var key = "log-miss";
        RedisDbMock.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        await Cache.GetAsync<string>(key);

        Assert.Contains(Logger.Messages, m => m.Contains("Cache miss"));
    }
}
