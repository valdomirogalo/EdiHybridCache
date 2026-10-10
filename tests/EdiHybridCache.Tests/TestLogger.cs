using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EdiHybridCache.Tests;

/// <summary>
/// Minimal in-memory logger that records the formatted message of every log call,
/// allowing tests to assert that expected log paths are actually exercised.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IEnumerable<string> Messages => _messages;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _messages.Enqueue(formatter(state, exception));

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
