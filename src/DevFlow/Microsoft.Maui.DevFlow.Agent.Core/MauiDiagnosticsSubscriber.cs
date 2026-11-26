using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml.Diagnostics;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Snapshots diagnostics without retaining views, binding sources, event arguments or exceptions.
/// The bounded queue keeps log sinks and file I/O off the diagnostic publisher's thread.
/// </summary>
internal sealed class MauiDiagnosticsSubscriber : IDisposable
{
    private const int MaxTextLength = 1024;
    private const int MaxTypes = 32;
    private readonly object _gate = new();
    private readonly object _flushGate = new();
    private readonly Queue<FileLogEntry> _pending = new();
    private readonly List<(EventInfo Event, Delegate Handler)> _hotReloadSubscriptions = [];
    private readonly List<string> _capturedEvents = ["visual-tree.added", "visual-tree.removed", "binding.failed"];
    private readonly Action<FileLogEntry> _write;
    private readonly int _capacity;
    private readonly Timer? _timer;
    private bool _disposed;
    private long _sequence;
    private long _dropped;

    internal IReadOnlyList<string> CapturedEvents => _capturedEvents;

    internal MauiDiagnosticsSubscriber(
        Action<FileLogEntry> write,
        int capacity = 256,
        bool startTimer = true,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] Type? hotReloadType = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _write = write;
        _capacity = capacity;
        VisualDiagnostics.VisualTreeChanged += OnVisualTreeChanged;
        BindingDiagnostics.BindingFailed += OnBindingFailed;
        SubscribeHotReload(hotReloadType ?? Type.GetType(
            "Microsoft.Maui.Controls.Xaml.Diagnostics.HotReloadDiagnostics, Microsoft.Maui.Controls"));
        Enqueue("capture.started", new JsonObject
        {
            ["events"] = new JsonArray(_capturedEvents.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
            ["queueCapacity"] = capacity,
            ["debuggerAttached"] = System.Diagnostics.Debugger.IsAttached
        });
        if (startTimer)
            _timer = new Timer(_ => Flush(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    private void OnVisualTreeChanged(object? sender, VisualTreeChangeEventArgs e)
    {
        try
        {
            Enqueue(e.ChangeType == VisualTreeChangeType.Add ? "visual-tree.added" : "visual-tree.removed",
                new JsonObject
                {
                    ["parent"] = Describe(e.Parent),
                    ["child"] = Describe(e.Child),
                    ["childIndex"] = e.ChildIndex
                });
        }
        catch
        {
            // Diagnostics must never interrupt the application.
        }
    }

    private void OnBindingFailed(object? sender, BindingBaseErrorEventArgs e)
    {
        try
        {
            var snapshot = new JsonObject
            {
                ["errorCode"] = Text(e.ErrorCode),
                ["messageTemplate"] = Text(e.Message),
                ["bindingPath"] = e.Binding is Binding binding ? Text(binding.Path) : null,
                ["sourceLocation"] = Source(e.XamlSourceInfo)
            };
            if (e is BindingErrorEventArgs error)
            {
                snapshot["target"] = Describe(error.Target);
                snapshot["targetProperty"] = Text(error.TargetProperty?.PropertyName);
                snapshot["sourceType"] = Text(error.Source?.GetType().FullName);
            }
            // MessageArgs can hold arbitrary application objects and values. Do not format them.
            Enqueue("binding.failed", snapshot, "Warning");
        }
        catch
        {
        }
    }

    private void SubscribeHotReload(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] Type? diagnosticsType)
    {
        foreach (var (eventName, kind) in new[]
        {
            ("UpdateRequested", "hot-reload.requested"),
            ("UpdateApplied", "hot-reload.applied"),
            ("UpdateFailed", "hot-reload.failed"),
            ("UpdateSkipped", "hot-reload.skipped")
        })
        {
            try
            {
                var eventInfo = diagnosticsType?.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
                if (eventInfo?.EventHandlerType is not { } handlerType)
                    continue;

                EventHandler<EventArgs> callback = (_, args) => OnHotReload(kind, args);
                var handler = Delegate.CreateDelegate(handlerType, callback.Target, callback.Method);
                eventInfo.AddEventHandler(null, handler);
                _hotReloadSubscriptions.Add((eventInfo, handler));
                _capturedEvents.Add(kind);
            }
            catch (Exception ex) when (ex is ArgumentException or MemberAccessException
                or NotSupportedException or TypeLoadException or TargetInvocationException)
            {
                // Older MAUI releases, or trimmed applications, may not expose these optional events.
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Optional Hot Reload payload properties are best-effort; unavailable trimmed members are omitted.")]
    private void OnHotReload(string kind, EventArgs args)
    {
        try
        {
            var snapshot = new JsonObject();
            foreach (var name in new[] { "UpdatedTypes", "HandledTypes", "InstanceCount", "FromVersion",
                "ToVersion", "Version", "Duration", "Timestamp", "UpdatedType", "Instance", "Exception" })
            {
                var value = args.GetType().GetProperty(name)?.GetValue(args);
                var key = name == "Duration" ? "durationMs" : char.ToLowerInvariant(name[0]) + name[1..];
                snapshot[key] = value switch
                {
                    null => null,
                    int number => JsonValue.Create(number),
                    TimeSpan duration => JsonValue.Create(duration.TotalMilliseconds),
                    DateTimeOffset timestamp => JsonValue.Create(timestamp),
                    Type type => JsonValue.Create(Text(type.FullName)),
                    IEnumerable<Type> types => new JsonArray(types.Take(MaxTypes)
                        .Select(type => (JsonNode?)JsonValue.Create(Text(type.FullName))).ToArray()),
                    _ when name == "Instance" => Describe(value),
                    _ when name == "Exception" => JsonValue.Create(Text(value.GetType().FullName)),
                    _ => null
                };
            }
            Enqueue(kind, snapshot, kind == "hot-reload.failed" ? "Warning" : "Information");
        }
        catch
        {
        }
    }

    private static JsonObject? Describe(object? value)
    {
        if (value == null)
            return null;
        return new JsonObject
        {
            ["type"] = Text(value.GetType().FullName),
            // A process-local hash is not a DevFlow tree ID and is not guaranteed unique.
            ["runtimeHash"] = RuntimeHelpers.GetHashCode(value),
            ["automationId"] = value is Element element ? Text(element.AutomationId) : null,
            ["sourceLocation"] = Source(VisualDiagnostics.GetSourceInfo(value))
        };
    }

    private static JsonObject? Source(SourceInfo? source) => source == null ? null : new JsonObject
    {
        ["uri"] = Text(source.SourceUri?.OriginalString),
        ["line"] = source.LineNumber,
        ["column"] = source.LinePosition
    };

    private static string? Text(string? text) => text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;

    private void Enqueue(string kind, JsonObject snapshot, string level = "Information")
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            snapshot["kind"] = kind;
            snapshot["sequence"] = ++_sequence;
            if (_pending.Count == _capacity)
            {
                _pending.Dequeue();
                _dropped++;
            }
            _pending.Enqueue(new FileLogEntry(DateTime.UtcNow, level, "Microsoft.Maui.Diagnostics",
                snapshot.ToJsonString(), Source: "maui-diagnostics"));
        }
    }

    internal void Flush()
    {
        lock (_flushGate)
        {
            FileLogEntry[] entries;
            long dropped;
            lock (_gate)
            {
                entries = _pending.ToArray();
                _pending.Clear();
                dropped = _dropped;
                _dropped = 0;
            }
            if (dropped > 0)
                Write(new FileLogEntry(DateTime.UtcNow, "Warning", "Microsoft.Maui.Diagnostics",
                    new JsonObject { ["kind"] = "capture.dropped", ["count"] = dropped }.ToJsonString(),
                    Source: "maui-diagnostics"));
            foreach (var entry in entries)
                Write(entry);
        }
    }

    private void Write(FileLogEntry entry)
    {
        try { _write(entry); }
        catch
        {
            // A disconnected or failing log consumer must not escape a timer or disposal callback.
        }
    }

    public void Dispose()
    {
        lock (_flushGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            _timer?.Dispose();
            VisualDiagnostics.VisualTreeChanged -= OnVisualTreeChanged;
            BindingDiagnostics.BindingFailed -= OnBindingFailed;
            foreach (var (eventInfo, handler) in _hotReloadSubscriptions)
                eventInfo.RemoveEventHandler(null, handler);
            _hotReloadSubscriptions.Clear();
            Flush();
        }
    }
}
