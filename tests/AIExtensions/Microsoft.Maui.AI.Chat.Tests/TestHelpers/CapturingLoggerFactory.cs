// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>Records every log entry and can throw when an entry at a given level is written.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
{
    internal List<LogEntry> Entries { get; } = new();

    /// <summary>Raised after an entry is recorded, on the thread that wrote it.</summary>
    internal event Action<LogEntry>? Logged;

    internal LogLevel? ThrowAtLevel { get; init; }

    internal InvalidOperationException LoggingException { get; } = new("logging failed");

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry(logLevel, eventId, formatter(state, exception), exception);
        Entries.Add(entry);
        Logged?.Invoke(entry);
        if (logLevel == ThrowAtLevel)
            throw LoggingException;
    }

    internal sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);
}
