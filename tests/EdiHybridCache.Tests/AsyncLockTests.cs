using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class AsyncLockTests
{
    [Fact]
    public async Task LockAsync_WhenContended_ShouldWaitUntilReleased()
    {
        var asyncLock = new AsyncLock();
        const string key = "contended-key";

        var first = await asyncLock.LockAsync(key);
        var second = asyncLock.LockAsync(key);

        // The second acquire must wait (slow path) because the first still holds the stripe.
        Assert.False(second.IsCompleted);

        first.Dispose();

        var releaser = await second;
        releaser.Dispose();
    }

    [Fact]
    public async Task LockAsync_WhenCancelled_ShouldThrow()
    {
        var asyncLock = new AsyncLock();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await asyncLock.LockAsync("key", cts.Token));
    }

    [Fact]
    public async Task LockAsync_DifferentKeys_ShouldUseDifferentStripes()
    {
        var asyncLock = new AsyncLock();

        var a = await asyncLock.LockAsync("key-a");
        // A different key may map to a different stripe; this must not deadlock.
        var b = await asyncLock.LockAsync("key-b");

        a.Dispose();
        b.Dispose();
    }
}
