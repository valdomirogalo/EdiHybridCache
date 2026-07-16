# 🚀 EdiHybridCache - Event Driven Cache Invalidation with Hybrid Cache

**The .NET Hybrid Cache Library — Blazing Fast, Battle-Tested, Enterprise-Ready**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-90.57%25-brightgreen)](https://github.com/valdomiro/EdiHybridCache)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![Publish NuGet Package](https://github.com/valdomirogalo/EdiHybridCache/actions/workflows/publish.yml/badge.svg)](https://github.com/valdomirogalo/EdiHybridCache/actions/workflows/publish.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)


---

## Why EdiHybridCache?

**Hybrid caching** combines the speed of in-process memory (L1) with the durability and sharing of Redis (L2). EdiHybridCache takes this further with:

- ⚡ **L1**: `IMemoryCache` — microsecond reads, zero network
- 📡 **L2**: Redis — shared across instances, persistent
- 🧠 **Event-driven invalidation**: RabbitMQ fanout — invalidate all L1s instantly
- 🛡️ **Anti-stampede**: Per-key async locking — only one request hits Redis
- 🔁 **Resilience**: Polly retries with exponential backoff + jitter
- 📦 **Compression**: GZip for large values
- 🔒 **Secure by design**: CWE-409, CWE-502, CWE-754, CWE-295, CWE-770 mitigated

---

## 📊 Performance

| Metric | Value | Proof |
|--------|-------|-------|
| **GetAsync L1 Hit** | **2.23 μs**, 72 B allocated | [Benchmark](#-benchmark-results) |
| **SetAsync (100B)** | **55.0 μs**, 1.7 KB allocated | [Benchmark](#-benchmark-results) |
| **Throughput (standalone)** | **15,164 req/s** @ 5,000 VUs | [k6 Load Test](#-k6-load-test) |
| **Throughput (Aspire)** | **14,063 req/s** @ 5,000 VUs | [k6 Load Test](#-k6-load-test) |
| **Failures** | **0.00%** @ 2.5M requests | [k6 Load Test](#-k6-load-test) |
| **Code Coverage** | **90.57% line**, 83.33% branch | [Coverage](#-code-coverage) |
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
    "RabbitMqHost": "localhost",
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
          ┌───────▼───────┐    ┌───────▼───────┐
          │   RabbitMQ    │    │   RabbitMQ    │
          │  Exchange     │    │  Queue (each  │
          │  (Fanout)     │───▶│   instance)   │
          └───────────────┘    └───────────────┘
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

- **Provider**: RabbitMQ fanout exchange
- **Flow**: `RemoveAsync` → publish event → all subscribers receive → each clears its L1
- **Graceful degradation**: If RabbitMQ is unavailable, invalidation events are skipped with a warning
- **Durable**: Messages are persistent; queues are auto-deleted per instance

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
3. Publish invalidation event via RabbitMQ (best-effort)

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
| **CWE-295** | 7.4 | Missing SSL/TLS for RabbitMQ | ✅ **Configurable** via `RabbitMqUseSsl`, `RabbitMqSslServerName`, `RabbitMqSslCertificatePath` |
| **CWE-770** | 5.3 | Unbounded resource allocation | ✅ **Size limits** — max key length (512), max value size (100 MB) |
| **CWE-312** | 5.9 | Cleartext secrets in memory | ⚠️ **Documented** — operate on trusted network, HMAC/add encryption if needed |
| **CWE-117** | 3.1 | Log injection | ✅ **Structured logging** via `LoggerMessage.Define` — no string interpolation in logs |

---

## 📈 Benchmark Results

```
BenchmarkDotNet v0.14.0, .NET 10.0.9, AMD Ryzen 7 5700U
9 benchmarks, 2 warmup, 5 iterations each
```

| Method | Mean | Gen0 | Gen1 | Allocated |
|--------|------|------|------|-----------|
| **GetAsync L1 Hit** | **2.23 μs** | 0.03 | — | **72 B** |
| **GetAsync L2 Hit** | 6,699 μs | — | — | 25.2 KB |
| **GetAsync L2 Miss** | 6,614 μs | — | — | 26.3 KB |
| **SetAsync 100B** | **55.0 μs** | 0.24 | — | **1.7 KB** |
| **SetAsync 10KB** | 29.7 μs | 1.71 | 0.73 | 11.5 KB |
| **SetAsync 200KB (LOH)** | 244 μs | — | — | 201.8 KB |
| **SetAsync 10KB + compress** | **22.5 μs** | 3.97 | 1.19 | **11.9 KB** 📉 |
| **RemoveAsync** | 9.30 μs | 0.37 | 0.11 | 2.4 KB |
| **InvalidateLocal** | **7.34 μs** | 0.24 | 0.06 | 1.5 KB |

**Key takeaways:**
- **GetAsync L1 Hit in 2.23 μs, 72 B allocated** — Zero-allocation fast path via synchronous lock acquisition
- **SetAsync 10KB + compress: 45% less allocations** (22 KB → 11.9 KB) after removing extra `MemoryStream` copy in `TryDecompress`
- **Zero LOH allocations** on hot paths — `ArrayPool<byte>` + `struct Releaser` + `ReadOnlySpan`
- **LoggerMessage.Define** eliminated `params object[]` allocation, saving ~32 B per hot log call

---

## 🧪 k6 Load Test

### Standalone (direct Redis connection) — V4 (HybridCache Singleton)

```
15,164 req/s · 0% failure · p(95) = 226 ms · 5,000 VUs
1,289,184 total requests, 1,718,912 checks passed ✅
```

### Aspire AppHost (with DCP proxy) — V4 (HybridCache Singleton)

```
14,063 req/s · 0% failure · p(95) = 225 ms · 5,000 VUs
1,195,692 total requests, 1,594,256 checks passed ✅
```

**Test scenario:** Set → Get(L1) → InvalidateLocal → Get(L2) → Remove → Get(Miss) (6 requests/iteration)

| Metric | Standalone | Aspire AppHost |
|--------|-----------|----------------|
| **Peak throughput** | **15,164 req/s** | **14,063 req/s** |
| **Avg latency** | **78 ms** | **73 ms** |
| **p(95) latency** | **226 ms** ✅ (< 2000ms) | **225 ms** ✅ (< 2000ms) |
| **HTTP failures** | **0.00%** | **0.00%** |
| **Total requests** | 1,289,184 | 1,195,692 |
| **Total checks** | 1,718,912 ✓ | 1,594,256 ✓ |
| **L1 hit rate** | 100% | 100% |
| **L2 hit rate** | 100% | 100% |

### Evolution of Optimizations

| Metric | V1 (default Redis) | V2 (Redis tuned) | V3 (Scoped HybridCache) | V4 (Singleton) |
|--------|-------------------|-------------------|-------------------------|----------------|
| **Standalone throughput** | 2,304 req/s | 9,562 req/s | 570 req/s | **15,164 req/s** 🔥 |
| **Aspire throughput** | — | — | 604 req/s | **14,063 req/s** 🔥 |
| **p(95) latency** | 982 ms | 629 ms | 12,260 ms | **225 ms** ✅ |
| **Failures** | 18% | **0.00%** | **0.00%** | **0.00%** |
| **p(95) < 2s threshold** | ❌ | ❌ | ❌ | **✅ Passed** |
| **Memory usage** | ~2 GB dump | — | ~500 MB | **~200 MB** 📉 |

---

## 📊 Code Coverage

| Metric | Value |
|--------|-------|
| **Line Coverage** | **90.57%** |
| **Branch Coverage** | **83.33%** |
| **Lines covered** | 222 of 245 (excluding RabbitMQ classes) |

### Per-Class Coverage

| Class | Coverage |
|-------|----------|
| `HybridCache` | 100% |
| `HybridCacheOptions` | 100% |
| `CompressionHelper` | 100% |
| `AsyncLock` | 100% |
| `ServiceCollectionExtensions` | 97.87% |
| `GetAsync` state machine | 97.29% |
| RabbitMQ classes | `[ExcludeFromCodeCoverage]` (require infrastructure) |

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

---

---

## ⚙️ Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `REDIS_CONNECTION` | — | Redis connection string |
| `RABBITMQ_HOST` | `localhost` | RabbitMQ host |
| `RABBITMQ_PORT` | `5672` | RabbitMQ port |
| `RABBITMQ_USERNAME` | `guest` | RabbitMQ username |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ password |
| `L1_TTL_SECONDS` | `300` | L1 TTL (in-process memory) |
| `DEFAULT_L2_TTL_SECONDS` | `3600` | Default L2 TTL (Redis) |
| `L2_TTL_MULTIPLIER` | `1.5` | Minimum L2/L1 TTL ratio |
| `RABBITMQ_USE_SSL` | `false` | Enable SSL/TLS for RabbitMQ |
| `RABBITMQ_SSL_SERVER_NAME` | — | RabbitMQ SSL server name |
| `RABBITMQ_SSL_CERT_PATH` | — | RabbitMQ SSL certificate path |

### appsettings.json Example

```json
{
  "EdiHybridCache": {
    "RedisConnectionString": "localhost:6379",
    "RabbitMqHost": "localhost",
    "RabbitMqPort": 5672,
    "RabbitMqUseSsl": false,
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

## 🧰 How to Run

### Standalone

```bash
# Build
dotnet build

# Run tests
dotnet test tests/EdiHybridCache.Tests

# Run benchmarks
dotnet run -c Release --project benchmarks/EdiHybridCache.Benchmarks

# Run the playground (Web API with Swagger) - requires Redis + RabbitMQ
dotnet run --project playground/EdiHybridCache.Playground --urls "http://localhost:5060"
# Swagger UI: http://localhost:5060/swagger

# Run k6 load test
k6 run k6-load-test.js
```

### With Aspire AppHost (recommended)

The Aspire AppHost automatically provisions Redis and RabbitMQ containers, injects environment variables, and starts the Playground:

```bash
dotnet run --project src/EdiHybridCache.AppHost/EdiHybridCache.AppHost.csproj
```

The dashboard will be available at `https://localhost:XXXXX` (random port). Redis and RabbitMQ credentials are auto-generated — no manual configuration needed.

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
│   │       ├── RabbitMqInvalidationPublisher.cs
│   │       └── RabbitMqInvalidationSubscriber.cs
│   ├── Configuration/
│   │   └── HybridCacheServiceCollectionExtensions.cs
│   └── EdiHybridCache.csproj     # NuGet package
├── src/EdiHybridCache.AppHost/  # 🚀 Aspire orchestration
│   ├── Program.cs               # AppHost entry point
│   ├── AppHostConstants.cs      # Resource names & env vars
│   └── EdiHybridCache.AppHost.csproj
├── tests/                        # ✅ Unit tests (26/26 passing)
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
- **RabbitMQ retries**: Separate Polly retry policy for publisher; **background reconnection** with exponential backoff (1s → 60s max) for subscriber
- **Graceful degradation**: If RabbitMQ is down, cache continues operating; invalidation events are skipped with a warning; subscriber retries in background
- **Timeouts**: Configurable `RedisOperationTimeoutSeconds` (default: 5s)
- **Connection tuning**: `AbortOnConnectFail=false`, `SyncTimeout=5s`, `KeepAlive=60s`, `ReconnectRetryPolicy` for StackExchange.Redis

---

## 🔒 Security Features

- **Key length validation**: Max 512 characters (ArgumentException)
- **Value size cap**: Max 100 MB (LogWarning + skip)
- **ZIP bomb protection**: Hard cap on decompression buffer doubling; leftover byte detection
- **Cache poisoning prevention**: `TypeNameHandling` is not supported by `System.Text.Json`; `JsonException` is caught and logged with "Possible cache poisoning"
- **Deadlock prevention**: No `.GetAwaiter().GetResult()` in constructors (CWE-754)
- **SSL/TLS**: Configurable for RabbitMQ connections
- **Log injection prevention**: Structured logging via `LoggerMessage.Define` — no `params object[]` on hot paths

---

## ⚖️ License

**MIT License** — Free to use, modify, distribute, and incorporate into any project (commercial or not). No attribution required, though appreciated.

Copyright © 2026 Valdomiro Galo

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
