namespace EdiHybridCache.Cache.Invalidation;

public interface ICacheInvalidationPublisher : IDisposable
{
    Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default);
}
