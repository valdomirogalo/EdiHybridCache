namespace EdiHybridCache.Cache.Invalidation;

public interface ICacheInvalidationSubscriber : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();
}
