using FluentAssertions;
using Moq;
using Xunit;
using EdiHybridCache.Cache.Invalidation;

namespace EdiHybridCache.Tests;

public class InvalidationTests : TestBase
{
    [Fact]
    public async Task PublishInvalidationAsync_ShouldCallPublisher()
    {
        var key = "publish-key";
        await Cache.PublishInvalidationAsync(key);
        PublisherMock.Verify(x => x.PublishInvalidationAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateLocal_ShouldRemoveFromL1()
    {
        var key = "local-invalidate";
        await Cache.SetAsync(key, "value");

        var before = await Cache.GetAsync<string>(key);
        before.Should().Be("value");

        Cache.InvalidateLocal(key);

        var after = await Cache.GetAsync<string>(key);
        after.Should().BeNull();
    }
}
