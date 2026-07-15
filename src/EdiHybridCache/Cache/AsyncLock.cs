using System.Collections.Concurrent;

namespace EdiHybridCache.Cache;

internal class AsyncLock
{
    private const int MaxConcurrency = 1;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<Releaser> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(MaxConcurrency, MaxConcurrency));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore, key, _locks);
    }

    internal readonly struct Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly string _key;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;

        public Releaser(SemaphoreSlim semaphore, string key, ConcurrentDictionary<string, SemaphoreSlim> locks)
        {
            _semaphore = semaphore;
            _key = key;
            _locks = locks;
        }

        public void Dispose()
        {
            _semaphore.Release();

            // If no one is waiting, remove the entry from the dictionary
            if (_semaphore.CurrentCount == MaxConcurrency)
                _locks.TryRemove(_key, out _);
        }
    }
}
