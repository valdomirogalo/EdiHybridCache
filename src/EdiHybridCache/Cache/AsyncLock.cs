namespace EdiHybridCache.Cache;

/// <summary>
/// Stripe-based async lock with O(1) memory (always exactly StripeCount semaphores).
/// Eliminates unbounded dictionary growth from the previous ConcurrentDictionary approach.
/// Collisions are resolved by hashing the key; the lock is released immediately after the
/// L2 read completes, so contention across stripes remains low in practice.
/// </summary>
internal class AsyncLock
{
    // Sourced from Constants.AsyncLockStripeCount — single source of truth
    private readonly SemaphoreSlim[] _stripes;

    public AsyncLock()
    {
        _stripes = new SemaphoreSlim[Constants.AsyncLockStripeCount];
        for (var i = 0; i < Constants.AsyncLockStripeCount; i++)
            _stripes[i] = new SemaphoreSlim(1, 1);
    }

    public Task<Releaser> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var stripe = (uint)key.GetHashCode(StringComparison.Ordinal) % Constants.AsyncLockStripeCount;
        var semaphore = _stripes[stripe];
        return LockSemaphoreAsync(semaphore, cancellationToken);
    }

    private static async Task<Releaser> LockSemaphoreAsync(
        SemaphoreSlim semaphore, CancellationToken cancellationToken)
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
