using System.Runtime.CompilerServices;

namespace EdiHybridCache.Cache;

/// <summary>
/// Stripe-based async lock with O(1) memory (always exactly StripeCount semaphores).
/// Uses a fast path that acquires the lock synchronously — zero allocation in the
/// uncontended case. Only when the stripe is contended does it fall back to an async
/// wait, which allocates. See also: <see cref="Constants.AsyncLockStripeCount"/>.
/// </summary>
internal class AsyncLock
{
    private readonly SemaphoreSlim[] _stripes;

    public AsyncLock()
    {
        _stripes = new SemaphoreSlim[Constants.AsyncLockStripeCount];
        for (var i = 0; i < Constants.AsyncLockStripeCount; i++)
            _stripes[i] = new SemaphoreSlim(1, 1);
    }

    public ValueTask<Releaser> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var stripe = (uint)key.GetHashCode(StringComparison.Ordinal) % Constants.AsyncLockStripeCount;
        var semaphore = _stripes[stripe];

        // Fast path: acquire synchronously — zero allocation.
        // With AsyncLockStripeCount = 16384 and typical concurrency, this succeeds >99% of the time.
        // CA2016: Wait(0, CancellationToken.None) — the fast path intentionally does not
        // propagate the caller's cancellation token because the synchronous wait is
        // near-instantaneous (single interlocked decrement) and never blocks asynchronously.
        if (semaphore.Wait(0, CancellationToken.None))
            return new ValueTask<Releaser>(new Releaser(semaphore));

        // Slow path: stripe is contended, fall back to async wait.
        return LockSlowAsync(semaphore, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async ValueTask<Releaser> LockSlowAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    internal readonly struct Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}
