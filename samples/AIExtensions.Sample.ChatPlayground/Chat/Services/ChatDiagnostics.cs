using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground;

public sealed record ChatDiagnosticEntry(
    long Sequence, string Heading, string Message, string Details, string? TraceId = null);

/// <summary>A bounded local receiver for existing AI logs and completed chat activities.</summary>
public sealed class ChatDiagnostics : ILoggerProvider
{
    public const string SourceName = "AIExtensions.Sample.ChatPlayground.Chat";
    public const int Capacity = 500;
    private const int TextLimit = 8192;
    private readonly object _gate = new();
    private readonly Queue<ChatDiagnosticEntry> _entries = new();
    private readonly ActivityListener _listener;
    private long _version;
    private bool _disposed;

    public ChatDiagnostics()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = CaptureActivity,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ILogger CreateLogger(string categoryName) => new ReceiverLogger(this, categoryName);

    public (long Version, ChatDiagnosticEntry[] Entries) Snapshot()
    {
        lock (_gate)
            return (_version, _entries.ToArray());
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _entries.Clear();
            _version++;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _entries.Clear();
            _version++;
        }
        _listener.Dispose();
    }

    private void Append(string heading, string message, string details, string? traceId)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_entries.Count == Capacity)
                _entries.Dequeue();
            _entries.Enqueue(new(++_version, Limit(heading), Limit(message), Limit(details), traceId));
        }
    }

    private void CaptureActivity(Activity activity)
    {
        var summary = string.Join(" | ", activity.TagObjects.Where(tag => tag.Key is
            "gen_ai.response.model" or "gen_ai.usage.input_tokens" or "gen_ai.usage.output_tokens")
            .Select(tag => $"{tag.Key}: {Convert.ToString(tag.Value, CultureInfo.InvariantCulture)}"));
        var tags = string.Join("\n", activity.TagObjects.Take(64).Select(tag =>
            $"{Limit(tag.Key)}: {Limit(Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? "")}"));
        Append(
            $"{activity.StartTimeUtc.ToLocalTime():HH:mm:ss.fff} | Telemetry | {activity.Source.Name}",
            $"{activity.DisplayName} | {activity.Duration.TotalMilliseconds:F1} ms | {activity.Status}" +
                (summary.Length == 0 ? "" : "\n" + summary),
            $"{Correlation(activity)}\n{activity.StatusDescription}\n{tags}", GetTraceId(activity));
    }

    private static string? GetTraceId(Activity? activity) =>
        activity is not null && activity.TraceId != default ? activity.TraceId.ToString() : null;

    private static string Correlation(Activity? activity) => activity is null
        ? string.Empty
        : $"Trace: {activity.TraceId} | Span: {activity.SpanId}";

    private static string Limit(string text) => text.Length <= TextLimit
        ? text : text[..TextLimit] + "\n[truncated]";

    private sealed class ReceiverLogger(ChatDiagnostics owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level)
        {
            lock (owner._gate)
                return !owner._disposed && level >= LogLevel.Debug && level < LogLevel.None
                    && category.StartsWith("Microsoft.Extensions.AI.", StringComparison.Ordinal);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var activity = Activity.Current;
            owner.Append(
                $"{DateTimeOffset.Now:HH:mm:ss.fff} | {logLevel} | {category} | {eventId}",
                formatter(state, exception),
                $"{Correlation(activity)}{(exception is null ? "" : "\n" + exception)}", GetTraceId(activity));
        }
    }
}
