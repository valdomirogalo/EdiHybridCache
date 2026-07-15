using System.ComponentModel;
using EdiHybridCache.Cache;
using EdiHybridCache.Configuration;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "EdiHybridCache API",
        Version = "v1",
        Description = "Playground for EdiHybridCache — a hybrid L1 (memory) + L2 (Redis) caching library with RabbitMQ invalidation.\n\n" +
                      "## Cache Architecture\n" +
                      "- **L1**: `IMemoryCache` (in-process, fast, per-instance)\n" +
                      "- **L2**: Redis (shared across instances)\n" +
                      "- **Invalidation**: RabbitMQ fanout exchange (remote L1 invalidation)\n\n" +
                      "## Behavioral Notes\n" +
                      "- `SetAsync` always writes to L1 first, then L2 (Redis) asynchronously\n" +
                      "- `GetAsync` checks L1 → L2 with double-checked locking\n" +
                      "- TTL adjustment: if L2 TTL < L1 TTL × multiplier, it's raised to the minimum\n" +
                      "- Remote invalidation is best-effort: if RabbitMQ is down, L1 operates independently"
    });
});

builder.Services.AddEdiHybridCache(builder.Configuration);

var app = builder.Build();

// ──────────────────────────────────────────────
//  RabbitMQ Subscriber Startup
// ──────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.UseEdiHybridCacheSubscriberAsync();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("RabbitMQ invalidation subscriber started successfully.");
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "RabbitMQ subscriber failed to start. Cache will operate without remote invalidation.");
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "EdiHybridCache v1");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.MapControllers();

// ═══════════════════════════════════════════════════════════════════
//  GET /cache/{key}
//  Summary : Retrieve a cached value by key
//  Method  : IHybridCache.GetAsync<T>
//  Cache   : L1 → L2 (double-checked locking)
//  Returns : 200 with value, or 404 if not found
// ═══════════════════════════════════════════════════════════════════
app.MapGet("/cache/{key}", async (
    [Description("Cache key (max 512 chars)")] string key,
    IHybridCache cache) =>
{
    var value = await cache.GetAsync<string>(key);
    return value is not null ? Results.Ok(value) : Results.NotFound();
})
.WithName("GetCachedValue")
.WithSummary("Retrieve a cached value")
.WithDescription("Gets a value from the hybrid cache. Checks L1 (memory) first, then L2 (Redis) with double-checked locking.")
.Produces<string>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
;

// ═══════════════════════════════════════════════════════════════════
//  POST /cache/{key}
//  Summary : Store a value with custom TTL (10 minutes)
//  Method  : IHybridCache.SetAsync<T>
//  Cache   : L1 + L2
//  Notes   : TTL adjustment may apply if configured multiplier makes min > 10m
// ═══════════════════════════════════════════════════════════════════
app.MapPost("/cache/{key}", async (
    [Description("Cache key (max 512 chars)")] string key,
    [Description("Value wrapper with JSON body")] CacheValue body,
    IHybridCache cache) =>
{
    await cache.SetAsync(key, body.Value, TimeSpan.FromMinutes(10));
    return Results.Ok(new { key, ttl = "10m", value = body.Value });
})
.WithName("SetCachedValueWithTtl")
.WithSummary("Store a value with 10-minute TTL")
.WithDescription("Stores a value in both L1 (memory) and L2 (Redis). Uses a custom 10-minute TTL for L2. " +
                 "The L1 TTL is configured via appsettings (default: 300s). If 10m is below the minimum " +
                 "(L1 TTL × multiplier), the TTL is automatically adjusted upward.")
.Produces(StatusCodes.Status200OK)
;

// ═══════════════════════════════════════════════════════════════════
//  POST /cache/{key}/default
//  Summary : Store a value with default TTL (from configuration)
//  Method  : IHybridCache.SetAsync<T>
//  Cache   : L1 + L2
//  Notes   : Uses DefaultL2TtlSeconds from configuration (default: 3600s)
// ═══════════════════════════════════════════════════════════════════
app.MapPost("/cache/{key}/default", async (
    [Description("Cache key (max 512 chars)")] string key,
    [Description("Value wrapper with JSON body")] CacheValue body,
    IHybridCache cache) =>
{
    await cache.SetAsync(key, body.Value);
    return Results.Ok(new { key, ttl = "default", value = body.Value });
})
.WithName("SetCachedValueDefaultTtl")
.WithSummary("Store a value with default TTL")
.WithDescription("Stores a value using the configured DefaultL2TtlSeconds (default: 3600s / 1 hour). " +
                 "L1 TTL is always the configured L1TtlSeconds (default: 300s).")
.Produces(StatusCodes.Status200OK)
;

// ═══════════════════════════════════════════════════════════════════
//  DELETE /cache/{key}
//  Summary : Remove a value from cache and publish invalidation
//  Method  : IHybridCache.RemoveAsync
//  Cache   : L1 + L2 + invalidation event
//  Notes   : Published invalidation is best-effort (RabbitMQ may be offline)
// ═══════════════════════════════════════════════════════════════════
app.MapDelete("/cache/{key}", async (
    [Description("Cache key to remove")] string key,
    IHybridCache cache) =>
{
    await cache.RemoveAsync(key);
    return Results.NoContent();
})
.WithName("RemoveCachedValue")
.WithSummary("Remove a cached value")
.WithDescription("Removes the value from L1 (memory) and L2 (Redis), then publishes an invalidation event " +
                 "via RabbitMQ so other instances can remove their L1 copy. If RabbitMQ is offline, " +
                 "the invalidation is skipped and a warning is logged.")
.Produces(StatusCodes.Status204NoContent)
;

// ═══════════════════════════════════════════════════════════════════
//  POST /cache/invalidate/{key}
//  Summary : Publish invalidation event without clearing local cache
//  Method  : IHybridCache.PublishInvalidationAsync
//  Cache   : Invalidation event only (no local change)
//  Notes   : Useful when another source wrote directly to Redis
// ═══════════════════════════════════════════════════════════════════
app.MapPost("/cache/invalidate/{key}", async (
    [Description("Key to invalidate remotely")] string key,
    IHybridCache cache) =>
{
    await cache.PublishInvalidationAsync(key);
    return Results.Ok(new { key, action = "invalidation_published" });
})
.WithName("PublishInvalidation")
.WithSummary("Publish cache invalidation event")
.WithDescription("Sends an invalidation event via RabbitMQ without modifying the local cache. " +
                 "Other instances subscribed to the invalidation exchange will remove their L1 copy. " +
                 "This is useful when a value was written directly to Redis by another service.")
.Produces(StatusCodes.Status200OK)
;

// ═══════════════════════════════════════════════════════════════════
//  POST /cache/invalidate-local/{key}
//  Summary : Invalidate L1 (memory) only, without Redis or RabbitMQ
//  Method  : HybridCache.InvalidateLocal (extension, not in IHybridCache)
//  Cache   : L1 only
//  Notes   : Synchronous operation, no network involved
// ═══════════════════════════════════════════════════════════════════
app.MapPost("/cache/invalidate-local/{key}", (
    [Description("Key to invalidate locally")] string key,
    IHybridCache cache) =>
{
    if (cache is HybridCache hc)
    {
        hc.InvalidateLocal(key);
        return Results.Ok(new { key, action = "l1_invalidated" });
    }
    return Results.Problem("Cache instance does not support InvalidateLocal");
})
.WithName("InvalidateLocal")
.WithSummary("Invalidate L1 (memory) only")
.WithDescription("Removes the value from L1 (in-process memory) only, without contacting Redis or RabbitMQ. " +
                 "This is a synchronous operation with no network I/O. Requires the cache instance to be " +
                 "of type HybridCache (not available through the IHybridCache interface).")
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
;

app.Run();

// ──────────────────────────────────────────────
//  Types
// ──────────────────────────────────────────────

/// <summary>
/// Request body for storing a cache value. Uses JSON binding.
/// </summary>
/// <param name="Value">The value to cache</param>
public record CacheValue(string Value);
