namespace EdiHybridCache.AppHost;

/// <summary>
/// Constants used by the Aspire AppHost orchestration.
/// Resource names must match those expected by the Playground / library.
/// </summary>
internal static class AppHostConstants
{
    // ── Resource names ──────────────────────────────────────────
    public const string RedisName = "redis";
    public const string RabbitMqName = "rabbitmq";
    public const string RabbitMqUserParamName = "rabbitmq-username";
    public const string RabbitMqPasswordParamName = "rabbitmq-password";
    public const string PlaygroundName = "playground";

    // ── Environment variable names (must match library Constants) ─
    public const string EnvRedisConnection = "REDIS_CONNECTION";
    public const string EnvRabbitMqHost = "RABBITMQ_HOST";
    public const string EnvRabbitMqPort = "RABBITMQ_PORT";
    public const string EnvRabbitMqUsername = "RABBITMQ_USERNAME";
    public const string EnvRabbitMqPassword = "RABBITMQ_PASSWORD";

    // ── Default credentials ─────────────────────────────────────
    public const string DefaultRabbitMqUsername = "guest";
    public const string DefaultRabbitMqPassword = "guest";

    // ── Connection string suffix ────────────────────────────────
    public const string RedisSslSuffix = ",ssl=true,abortConnect=false,password=";
}
