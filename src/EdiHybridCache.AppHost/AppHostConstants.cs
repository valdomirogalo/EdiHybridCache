namespace EdiHybridCache.AppHost;

/// <summary>
/// Constants used by the Aspire AppHost orchestration.
/// Resource names must match those expected by the Playground / library.
/// </summary>
internal static class AppHostConstants
{
    // ── Resource names ──────────────────────────────────────────
    public const string RedisName = "redis";
    public const string PlaygroundName = "playground";

    // ── Environment variable names (must match library Constants) ─
    public const string EnvRedisConnection = "REDIS_CONNECTION";

    // ── Connection string suffix ────────────────────────────────
    public const string RedisSslSuffix = ",ssl=true,abortConnect=false,password=";
}
