namespace EdiHybridCache.Cache;

public interface IHybridCache
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttlL2 = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task PublishInvalidationAsync(string key, CancellationToken cancellationToken = default);
}
