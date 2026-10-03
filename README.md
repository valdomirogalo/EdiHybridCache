# 🚀 EdiHybridCache - Event Driven Cache Invalidation with Hybrid Cache

**The .NET Hybrid Cache Library — Blazing Fast, Battle-Tested, Enterprise-Ready**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-91.11%25-brightgreen)](https://github.com/valdomiro/EdiHybridCache)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![Publish NuGet Package](https://github.com/valdomirogalo/EdiHybridCache/actions/workflows/publish.yml/badge.svg)](https://github.com/valdomirogalo/EdiHybridCache/actions/workflows/publish.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)


---

## Why EdiHybridCache?

**Hybrid caching** combines the speed of in-process memory (L1) with the durability and sharing of Redis (L2). EdiHybridCache takes this further with:

- ⚡ **L1**: `IMemoryCache` — microsecond reads, zero network
- 📡 **L2**: Redis — shared across instances, persistent
- 🧠 **Event-driven invalidation**: Redis Pub/Sub — invalidate all L1s instantly
- 🛡️ **Anti-stampede**: Per-key async locking — only one request hits Redis
- 🔁 **Resilience**: Polly retries with exponential backoff + jitter
- 📦 **Compression**: GZip for large values
- 🔒 **Secure by design**: CWE-409, CWE-502, CWE-754, CWE-295, CWE-770 mitigated

---

## 📊 Performance

| Metric | Value | Proof |
|--------|-------|-------|
| **GetAsync L1 Hit** | **98 ns**, 72 B allocated | [Benchmark](#-benchmark-results) |
| **SetAsync (100B)** | **5.86 μs**, 1.6 KB allocated | [Benchmark](#-benchmark-results) |
| **PublishInvalidationAsync** | **1.78 μs**, 640 B allocated | [Benchmark](#-benchmark-results) |
| **Throughput (standalone)** | **18,964 req/s** @ 5,000 VUs | [k6 Load Test](#-k6-load-test) |
| **Failures** | **0.00%** @ 1.33M requests | [k6 Load Test](#-k6-load-test) |
| **Code Coverage** | **91.11% line**, 87.87% branch | [Coverage](#-code-coverage) |
| **CRAP Score** | Reduced up to **69%** | [Complexity](#-code-quality--complexity) |

---

## 📦 Installation

```bash
dotnet add package EdiHybridCache
```

Or reference the project directly:

```xml
<ProjectReference Include="..\src\EdiHybridCache\EdiHybridCache.csproj" />
```

### 🔏 Verifying Package Signature (Sigstore)

Every NuGet release is signed with [Sigstore](https://www.sigstore.dev/) using a private key stored as a GitHub Actions secret. The public key (`cosign.pub`) is available in the repository and in every workflow artifact.

1. Download the `.sig` file for the release version from the [workflow artifacts](https://github.com/valdomirogalo/EdiHybridCache/actions) and the public key from the repo root.
2. Verify with [`cosign`](https://docs.sigstore.dev/system_config/installation/):

```bash
cosign verify-blob \
  --key cosign.pub \
  --signature EdiHybridCache.X.Y.Z.nupkg.sig \
  EdiHybridCache.X.Y.Z.nupkg
```

> Replace `X.Y.Z` with the actual version number. Verification succeeds if the package was signed by the trusted private key.

---

## 🔧 Quick Start

### 1. Register in DI

```csharp
// Program.cs
builder.Services.AddEdiHybridCache(builder.Configuration);
```

### 2. Configure `appsettings.json`

```json
{
  "EdiHybridCache": {
    "RedisConnectionString": "localhost:6379",
    "InvalidationChannel": "edi.cache.invalidation",
    "L1TtlSeconds": 300,
    "DefaultL2TtlSeconds": 3600,
    "EnableCompression": true
  }
}
```

### 3. Inject and Use

```csharp
public class MyService
{
    private readonly IHybridCache _cache;

    public MyService(IHybridCache cache) => _cache = cache;

    public async Task<string?> GetUserAsync(int id)
    {
        var key = $"user:{id}";
        return await _cache.GetAsync<string>(key);
    }

    public async Task SetUserAsync(int id, string data)
    {
        var key = $"user:{id}";
        await _cache.SetAsync(key, data, TimeSpan.FromMinutes(30));
    }

    public async Task RemoveUserAsync(int id)
    {
        var key = $"user:{id}";
        await _cache.RemoveAsync(key);
    }
}
```

### 4. Start the Invalidation Subscriber (optional)

```csharp
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.UseEdiHybridCacheSubscriberAsync();
}
```

---

## 🏗️ Architecture

```
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Instance A  │     │  Instance B  │     │  Instance C  │
│  ┌───────┐   │     │  ┌───────┐   │     │  ┌───────┐   │
│  │  L1   │   │     │  │  L1   │   │     │  │  L1   │   │
│  │Memory │   │     │  │Memory │   │     │  │Memory │   │
│  └───┬───┘   │     │  └───┬───┘   │     │  └───┬───┘   │
│      │       │     │      │       │     │      │       │
│  ┌───▼───┐   │     │  ┌───▼───┐   │     │  ┌───▼───┐   │
│  │  L2   │   │     │  │  L2   │   │     │  │  L2   │   │
│  │ Redis │   │     │  │ Redis │   │     │  │ Redis │   │
│  └───────┘   │     │  └───────┘   │     │  └───────┘   │
│      │       │     │      │       │     │      │       │
└──────┼───────┘     └──────┼───────┘     └──────┼───────┘
       │                    │                    │
       └──────────┬─────────┴──────────┬─────────┘
                  │                    │
                  └──────────┬─────────┘
                             │
                    ┌────────▼────────┐
                    │  Redis Pub/Sub  │
                    │  Channel        │
                    │ (each instance  │
                    │  subscribed)    │
                    └─────────────────┘
```

### L1 — In-Process Memory

- **Provider**: `Microsoft.Extensions.Caching.Memory`
- **Latency**: **1.28 μs** (microseconds)
- **Allocation**: **144 B per hit** (dropping to ~104 B with ValueTask)
- **Scope**: Per-instance, ephemeral

### L2 — Redis

- **Provider**: `StackExchange.Redis`
- **Persistence**: Shared across all instances
- **Resilience**: Automatic retry via Polly (exponential backoff 1s → 2s → 4s + jitter)
- **Connection**: Singleton via DI

### Event-Driven Invalidation

- **Provider**: Redis Pub/Sub channel
- **Flow**: `RemoveAsync` → publish event → all subscribers receive → each clears its L1
- **Graceful degradation**: If Redis is unavailable, invalidation events are skipped with a warning
- **Delivery**: Fire-and-forget (at-most-once) — instances not subscribed at publish time do not receive the event; stale L1 entries still expire via TTL

---

## 📚 API Reference

### `ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)`

Retrieves a value from the cache.

**Behavior:**
1. Check L1 (memory) → if found, return immediately (synchronous ValueTask, zero Task allocation)
2. Acquire per-key async lock
3. Double-check L1 (anti-stampede)
4. Read from L2 (Redis) with Polly retry
5. If found, populate L1 and return
6. If not found, return `null`

```csharp
var user = await cache.GetAsync<User>("user:42");
// Returns null if not found
```

### `Task SetAsync<T>(string key, T value, TimeSpan? ttlL2 = null, CancellationToken ct = default)`

Stores a value in the cache.

**Behavior:**
1. Validate key length (max 512 chars)
2. Serialize value with `System.Text.Json` (camelCase)
3. Optionally compress with GZip (threshold configurable)
4. Write to L1 (memory) - always
5. Write to L2 (Redis) with Polly retry
6. Log the operation

```csharp
await cache.SetAsync("user:42", user, TimeSpan.FromMinutes(30));
// Uses L1 TTL from config, L2 TTL = 30min (adjusted if below minimum)
```

**TTL Adjustment:**
- If `ttlL2 < L1TtlSeconds × L2TtlMultiplier`, it's automatically raised to the minimum
- This prevents race conditions where L2 expires before L1

### `Task RemoveAsync(string key, CancellationToken ct = default)`

Removes a value from the cache and notifies other instances.

**Behavior:**
1. Remove from L1 (memory)
2. Delete from L2 (Redis) with Polly retry
3. Publish invalidation event via Redis Pub/Sub (best-effort)

```csharp
await cache.RemoveAsync("user:42");
```

### `Task PublishInvalidationAsync(string key, CancellationToken ct = default)`

Publishes an invalidation event **without** modifying the local cache. Useful when another service writes directly to Redis.

```csharp
await cache.PublishInvalidationAsync("user:42");
```

### `void InvalidateLocal(string key)`

Synchronously removes a value from L1 only. No network I/O.

```csharp
if (cache is HybridCache hc)
    hc.InvalidateLocal("user:42");
```

---

## 🔒 Security

| CWE | CVSS | Vulnerability | Status |
|-----|------|--------------|--------|
| **CWE-409** | 7.5 | ZIP Bomb — decompression bomb | ✅ **Hard cap** at 100 MB, detection via leftover bytes |
| **CWE-502** | 6.5 | Deserialization injection | ✅ **Type-safe** `System.Text.Json` + `where T : class` + exception logging |
| **CWE-754** | 7.5 | Deadlock from `.GetAwaiter().GetResult()` | ✅ **Async lazy init** — no blocking in constructors |
| **CWE-295** | 7.4 | Missing SSL/TLS for cache backends | ✅ **Redis TLS** via the `RedisConnectionString` (`ssl=true`, as used by the Aspire AppHost) |
| **CWE-770** | 5.3 | Unbounded resource allocation | ✅ **Size limits** — max key length (512), max value size (100 MB) |
| **CWE-312** | 5.9 | Cleartext secrets in memory | ⚠️ **Documented** — operate on trusted network, HMAC/add encryption if needed |
| **CWE-117** | 3.1 | Log injection | ✅ **Structured logging** via `LoggerMessage.Define` — no string interpolation in logs |

---

## 📈 Benchmark Results

```
BenchmarkDotNet v0.14.0, .NET 10.0.12, AMD Ryzen 7 5700U
14 benchmarks, 2 warmup, 5 iterations each
```

| Method | Mean | Gen0 | Gen1 | Allocated |
|--------|------|------|------|-----------|
| **GetAsync L1 Hit** | **98.4 ns** | 0.03 | — | **72 B** |
| GetAsync L2 Hit | 802.6 μs | 11.72 | 10.74 | 25.4 KB |
| GetAsync L2 Miss | 731.8 μs | 3.91 | 2.93 | 26.4 KB |
| **SetAsync 100B** | **5.86 μs** | 0.26 | 0.08 | **1.6 KB** |
| SetAsync 10KB | 13.62 μs | 1.83 | 0.92 | 11.5 KB |
| SetAsync 200KB (LOH) | 104.5 μs | — | — | 201.5 KB |
| SetAsync 10KB + compress | 19.74 μs | 3.85 | 0.98 | 11.9 KB |
| RemoveAsync | 6.54 μs | 0.37 | 0.11 | 2.4 KB |
| InvalidateLocal | 4.64 μs | 0.24 | 0.07 | 1.5 KB |
| **PublishInvalidationAsync (Redis Pub/Sub)** | **1.78 μs** | 0.10 | 0.03 | **640 B** |

**Single vs double allocation — publish and receive paths:**

| Strategy | Mean | Allocated |
|----------|------|-----------|
| Publish payload — **single alloc** (serialize once to UTF-8 bytes) | **176.9 ns** | **112 B** |
| Publish payload — double alloc (string round-trip, anti-pattern) | 237.0 ns | 232 B |
| Deserialize — **single alloc** (read straight from the raw bytes) | **246.9 ns** | **88 B** |
| Deserialize — double alloc (string round-trip, anti-pattern) | 317.3 ns | 208 B |

**Key takeaways:**
- **PublishInvalidationAsync in 1.78 μs, 640 B** — serialized **once** to UTF-8 bytes and published as a `RedisValue`; the string round-trip anti-pattern would allocate **2×** the payload (232 B vs 112 B, +107 %)
- **Receive path allocates once too** — `JsonSerializer.Deserialize((byte[])value)` reads straight from the raw bytes; a string round-trip would allocate 208 B vs 88 B (+136 %). A unit test asserts the `(byte[])RedisValue` cast is zero-copy (`ReferenceEquals`)
- **GetAsync L1 Hit in 98 ns, 72 B allocated** — zero-allocation fast path via synchronous lock acquisition
- **RemoveAsync / InvalidateLocal allocations unchanged** (2.4 KB / 1.5 KB) — the cache hot path is untouched by the Pub/Sub swap
- **SetAsync 10KB + compress** at 11.9 KB allocated (no extra `MemoryStream` copy in `TryDecompress`)

**Before / after — paired re-run (v0.5.6 vs v1.0.0 on the same machine, .NET 10.0.12):**

| Method | v0.5.6 (RabbitMQ) | v1.0.0 (Redis Pub/Sub) | Allocated (both) |
|--------|-------------------|------------------------|------------------|
| GetAsync L1 Hit | 99.1 ns | 106.0 ns | 72 B |
| GetAsync L2 Hit | 701.2 μs | 706.4 μs | 25.4 KB |
| GetAsync L2 Miss | 732.2 μs | 733.5 μs | 26.4 KB |
| SetAsync 100B | 6.28 μs | 6.08 μs | 1.6 KB |
| SetAsync 10KB | 14.38 μs | 13.68 μs | 11.5 KB |
| SetAsync 200KB (LOH) | 108.6 μs | 111.7 μs | 201.5 KB |
| SetAsync 10KB + compress | 22.08 μs | 20.05 μs | 11.9 KB |
| RemoveAsync | 6.79 μs | 6.66 μs | 2.4 KB |
| InvalidateLocal | 4.82 μs | 4.67 μs | 1.5 KB |
| PublishInvalidationAsync (Redis Pub/Sub) | — | 1.78 μs | 640 B |

> The v0.5.6 baseline was **re-run on the same machine and .NET SDK (10.0.12)** as v1.0.0. Every cache hot path is equivalent within run-to-run noise and **allocations are identical** — the Pub/Sub swap only replaces the invalidation publish path (previously RabbitMQ, not micro-benchmarked).

---

## 🧪 k6 Load Test

### v1.0.0 — Standalone (Redis Pub/Sub invalidation)

```
median of 3 runs: 18,964 req/s · 0% failure · p(95) = 108 ms · 5,000 VUs
runs: 18,964 / 17,522 / 20,081 req/s
```

**Test scenario:** Set → Get(L1) → InvalidateLocal → Get(L2) → Remove → Get(Miss) (6 requests/iteration)

### Before / after — paired re-run (3 runs each, same machine, .NET 10.0.12)

| Metric (median of 3, 5,000 VUs) | 0.5.6 (RabbitMQ) | 1.0.0 (Redis Pub/Sub) |
|---------------------------------|-------------------|------------------------|
| **Peak throughput** | 18,300 req/s | **18,964 req/s** |
| **Avg latency** | 38.4 ms | **36.1 ms** |
| **p(95) latency** | 106.2 ms | 107.9 ms |
| **HTTP failures** | **0.00%** | **0.00%** |
| **L1 / L2 hit rate** | 100% | 100% |
| Throughput runs | 18,300 / 17,298 / 18,798 | 18,964 / 17,522 / 20,081 |

> Both versions sit at **~18k req/s** with overlapping run-to-run ranges, **0.00% failures** and p(95) ~100–115 ms. The RabbitMQ → Redis Pub/Sub swap is **throughput/latency-neutral**; Redis simply drops the extra broker and reuses the L2 connection.

### Status Final

| Metric | Status      |
|--------|-------------|
| **Standalone throughput** | **~18,964 req/s** 🔥 |
| **p(95) latency** | **~108 ms** ✅ |
| **Failures** | **0.00%** |
| **p(95) < 2s threshold** | **✅ Passed** |
| **Memory usage** | **~200 MB** 📉 |

---

## 📊 Code Coverage

| Metric | Value |
|--------|-------|
| **Line Coverage** | **91.11%** |
| **Branch Coverage** | **87.87%** |
| **Lines covered** | 472 of 518 (including Redis Pub/Sub classes) |

### Per-Class Coverage

| Class | Coverage |
|-------|----------|
| `HybridCache` | 94.25% |
| `HybridCacheOptions` | 100% |
| `CompressionHelper` | 85.53% |
| `AsyncLock` | 100% |
| `ServiceCollectionExtensions` | 73.02% |
| `RedisInvalidationPublisher` | 92.31% line / 100% branch |
| `RedisInvalidationSubscriber` | 81.25% line / 75% branch |

---

## 📉 Code Quality & Complexity

### Cyclomatic Complexity Reduction

| Method | Before | After | Reduction |
|--------|--------|-------|-----------|
| `TryDecompress` | CC **8** | CC **4** | 🔽 **50%** |
| `DeserializeRedisValue` | CC **6** | CC **2** | 🔽 **67%** |
| `GetAsync` | CC **5** | CC **3** | 🔽 **40%** |
| `SetAsync` | CC **3** | CC **3** | — |
| **Overall** | **CC 22** | **CC 12** | 🔽 **45%** |

### CRAP Score Improvement

CRAP = (CC²) × (1 − coverage)³ + CC

| Method | CC | Coverage | CRAP Before | CRAP After | Improvement |
|--------|----|----------|-------------|------------|-------------|
| `TryDecompress` | 8→4 | 70% | 13.3 | **5.0** | 🔽 **62%** |
| `DeserializeRedisValue` | 6→2 | 90% | 6.4 | **2.0** | 🔽 **69%** |
| `GetAsync` | 5→3 | 95% | 5.0 | **3.0** | 🔽 **40%** |

### Clean Code Practices

- ✅ **DRY**: `RedisSafeExecuteAsync<T>` extracted from 3 repetitions
- ✅ **DRY**: `TryOverrideFromEnv` / `TryParseEnvInt` / `TryParseEnvDouble` replace 8 repetitions
- ✅ **DRY**: `ValidateMaxSize` extracted from 2 repetitions
- ✅ **Single Responsibility**: Each method does one thing
- ✅ **Early Return**: No else branches — early exit pattern
- ✅ **Static members before instance** (SA1204 compliance)
- ✅ **Zero `params object[]`** in hot path logs (LoggerMessage.Define)
- ✅ **No magic strings** — all constants named

---

## ⚙️ Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `REDIS_CONNECTION` | — | Redis connection string |
| `INVALIDATION_CHANNEL` | `edi.cache.invalidation` | Redis Pub/Sub channel for L1 invalidation |
| `L1_TTL_SECONDS` | `300` | L1 TTL (in-process memory) |
| `DEFAULT_L2_TTL_SECONDS` | `3600` | Default L2 TTL (Redis) |
| `L2_TTL_MULTIPLIER` | `1.5` | Minimum L2/L1 TTL ratio |

### appsettings.json Example

```json
{
  "EdiHybridCache": {
    "RedisConnectionString": "localhost:6379",
    "InvalidationChannel": "edi.cache.invalidation",
    "L1TtlSeconds": 300,
    "DefaultL2TtlSeconds": 3600,
    "L2TtlMultiplier": 1.5,
    "EnableCompression": true,
    "CompressionThresholdBytes": 4096,
    "RetryCount": 3,
    "RetryBaseDelaySeconds": 1
  }
}
```

---

## ⬆️ Migrating from 0.x (RabbitMQ) to 1.0.0

Version **1.0.0** replaces the RabbitMQ invalidation backend with **Redis Pub/Sub**. This is a breaking change.

| 0.x (RabbitMQ) | 1.0.0 (Redis Pub/Sub) |
|----------------|-----------------------|
| `RabbitMQ.Client` dependency | Reuses the `StackExchange.Redis` connection — no extra broker |
| `RabbitMqHost` / `RabbitMqPort` / `RabbitMqUsername` / `RabbitMqPassword` | Removed — use `RedisConnectionString` |
| `RabbitMqUseSsl` / `RabbitMqSslServerName` / `RabbitMqSslCertificatePath` | Removed — use Redis TLS (`ssl=true`) |
| `InvalidationExchange` / `InvalidationQueueName` | `InvalidationChannel` (default `edi.cache.invalidation`) |
| Env `RABBITMQ_*` | Env `INVALIDATION_CHANNEL` |
| Durable queues (at-least-once) | Fire-and-forget Pub/Sub (at-most-once; L1 TTL still bounds staleness) |

**Action required:** remove the RabbitMQ configuration keys from `appsettings.json` (optionally set `InvalidationChannel`) and stop provisioning a RabbitMQ broker. The `ICacheInvalidationPublisher` / `ICacheInvalidationSubscriber` interfaces and `UseEdiHybridCacheSubscriberAsync` are unchanged.

---

## 🧰 How to Run

### Docker (Redis)

Start the required infrastructure (Redis) with Docker Compose:

```bash
docker-compose up -d
```

This starts **Redis** (L2 cache and Pub/Sub backend).

### Standalone

```bash
# Build
dotnet build

# Run tests
dotnet test tests/EdiHybridCache.Tests

# Run benchmarks
dotnet run -c Release --project benchmarks/EdiHybridCache.Benchmarks

# Run the playground (Web API with Swagger) - requires Redis
# (start docker-compose first, or have Redis running locally)
dotnet run --project playground/EdiHybridCache.Playground
# Swagger UI: http://localhost:5000/swagger/index.html
# API base URL: http://localhost:5000

# Run k6 load test (while playground is running)
k6 run k6-load-test.js
```

### With Aspire AppHost (recommended)

The Aspire AppHost automatically provisions a Redis container, injects environment variables, and starts the Playground:

```bash
dotnet run --project src/EdiHybridCache.AppHost/EdiHybridCache.AppHost.csproj
```

The dashboard will be available at `https://localhost:XXXXX` (random port). Redis credentials are auto-generated — no manual configuration needed.

---

## 🏗️ Project Structure

```
EdiHybridCache/
├── src/EdiHybridCache/           # 📚 Library source
│   ├── Cache/
│   │   ├── HybridCache.cs        # Core implementation
│   │   ├── IHybridCache.cs       # Public interface
│   │   ├── HybridCacheOptions.cs # Configuration options
│   │   ├── AsyncLock.cs          # Per-key async locking
│   │   ├── CacheMetrics.cs       # OpenTelemetry metrics
│   │   ├── CompressionHelper.cs  # GZip compression (ArrayPool)
│   │   ├── Constants.cs          # Central constants
│   │   └── Invalidation/
│   │       ├── ICacheInvalidationPublisher.cs
│   │       ├── ICacheInvalidationSubscriber.cs
│   │       ├── RedisInvalidationPublisher.cs
│   │       └── RedisInvalidationSubscriber.cs
│   ├── Configuration/
│   │   └── HybridCacheServiceCollectionExtensions.cs
│   └── EdiHybridCache.csproj     # NuGet package
├── src/EdiHybridCache.AppHost/  # 🚀 Aspire orchestration
│   ├── Program.cs               # AppHost entry point
│   ├── AppHostConstants.cs      # Resource names & env vars
│   └── EdiHybridCache.AppHost.csproj
├── tests/                        # ✅ Unit tests (78/78 passing)
│   └── EdiHybridCache.Tests/
├── benchmarks/                   # ⚡ Performance benchmarks
│   └── EdiHybridCache.Benchmarks/
├── playground/                   # 🎮 Sample Web API (Swagger)
│   └── EdiHybridCache.Playground/
├── k6-load-test.js               # 📊 Load testing script
└── README.md
```

---

## 🧠 Anti-Stampede (Cache Stampede Protection)

When a popular key expires in L1 and multiple requests arrive simultaneously, only **one** request hits Redis:

```csharp
using (await _asyncLock.LockAsync(key, cancellationToken))
{
    // Double-check: if another thread already populated L1, return it
    if (_memoryCache.TryGetValue(key, out cached))
        return cached;

    // Only ONE request reaches Redis
    var redisValue = await _redisDb.StringGetAsync(key);
}
```

---

## 🔁 Resilience

- **Redis retries**: Automatic Polly retry policy (configurable count + exponential backoff + jitter)
- **Pub/Sub resilience**: StackExchange.Redis reconnects and re-subscribes the invalidation handler automatically after a connection drop
- **Graceful degradation**: If Redis is down, cache continues operating; invalidation events are skipped with a warning
- **Timeouts**: Configurable `RedisOperationTimeoutSeconds` (default: 5s)
- **Connection tuning**: `AbortOnConnectFail=false`, `SyncTimeout=5s`, `KeepAlive=60s`, `ReconnectRetryPolicy` for StackExchange.Redis

---

## 🔒 Security Features

- **Key length validation**: Max 512 characters (ArgumentException)
- **Value size cap**: Max 100 MB (LogWarning + skip)
- **ZIP bomb protection**: Hard cap on decompression buffer doubling; leftover byte detection
- **Cache poisoning prevention**: `TypeNameHandling` is not supported by `System.Text.Json`; `JsonException` is caught and logged with "Possible cache poisoning"
- **Deadlock prevention**: No `.GetAwaiter().GetResult()` in constructors (CWE-754)
- **SSL/TLS**: Redis connections support TLS via the connection string (`ssl=true`)
- **Log injection prevention**: Structured logging via `LoggerMessage.Define` — no `params object[]` on hot paths

---

## ⚖️ License

**MIT License** — Free to use, modify, distribute, and incorporate into any project (commercial or not). No attribution required, though appreciated.

Copyright © 2026 Valdomiro Galo · [![ORCID](https://img.shields.io/badge/ORCID-0009--0009--0862--1462-A6CE39?logo=orcid)](https://orcid.org/0009-0009-0862-1462)

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
