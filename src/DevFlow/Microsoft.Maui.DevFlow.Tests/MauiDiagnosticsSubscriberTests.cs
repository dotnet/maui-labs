using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Tests;

[CollectionDefinition("MAUI diagnostic producers", DisableParallelization = true)]
public sealed class MauiDiagnosticProducersCollection;

[Collection("MAUI diagnostic producers")]
public sealed class MauiDiagnosticsSubscriberTests : IDisposable
{
    private const string DiagnosticsSwitch = "Microsoft.Maui.RuntimeFeature.EnableMauiDiagnostics";
    private readonly bool _previousSwitch;
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), $"devflow-diagnostics-{Guid.NewGuid():N}");

    public MauiDiagnosticsSubscriberTests()
    {
        AppContext.TryGetSwitch(DiagnosticsSwitch, out _previousSwitch);
        AppContext.SetSwitch(DiagnosticsSwitch, true);
    }

    public void Dispose()
    {
        AppContext.SetSwitch(DiagnosticsSwitch, _previousSwitch);
        if (Directory.Exists(_logDir))
            Directory.Delete(_logDir, recursive: true);
    }

    [Fact]
    public void VisualTreeEvents_CaptureDetachedSourceInformationAndRemoval()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);
        var child = new Label { AutomationId = "Caption" };
        VisualDiagnostics.RegisterSourceInfo(child, new Uri("file:///sample/MainPage.xaml"), 12, 4);

        VisualDiagnostics.OnChildAdded(null, child, 2);
        VisualDiagnostics.OnChildRemoved(null!, child, 2);
        subscriber.Flush();

        var added = Snapshot(Assert.Single(entries, e => Kind(e) == "visual-tree.added"));
        Assert.Equal("Caption", added["child"]!["automationId"]!.GetValue<string>());
        Assert.Equal(12, added["child"]!["sourceLocation"]!["line"]!.GetValue<int>());
        Assert.Equal(2, added["childIndex"]!.GetValue<int>());
        Assert.Contains(entries, e => Kind(e) == "visual-tree.removed");
    }

    [Fact]
    public void BindingFailure_UsesRealProducerWithoutFormattingSourceValues()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);
        var label = new Label { AutomationId = "BrokenCaption", BindingContext = new BindingSource() };

        label.SetBinding(Label.TextProperty, new Binding("MissingProperty"));
        subscriber.Flush();

        var entry = Assert.Single(entries, e => Kind(e) == "binding.failed");
        var snapshot = Snapshot(entry);
        Assert.Equal("Warning", entry.Level);
        Assert.Equal("MissingProperty", snapshot["bindingPath"]!.GetValue<string>());
        Assert.Equal("Text", snapshot["targetProperty"]!.GetValue<string>());
        Assert.Equal("BrokenCaption", snapshot["target"]!["automationId"]!.GetValue<string>());
        Assert.DoesNotContain("private-source-value", entry.Message);
        Assert.Null(snapshot["messageArgs"]);
    }

    [Fact]
    public void DisabledProducer_DoesNotBecomeEnabledBySubscription()
    {
        AppContext.SetSwitch(DiagnosticsSwitch, false);
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);

        VisualDiagnostics.OnChildAdded(null, new Label(), 0);
        var label = new Label { BindingContext = new BindingSource() };
        label.SetBinding(Label.TextProperty, new Binding("MissingProperty"));
        subscriber.Flush();

        Assert.All(entries, e => Assert.Equal("capture.started", Kind(e)));
    }

    [Fact]
    public void FullQueue_KeepsNewestSnapshotsAndReportsDroppedEvents()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries, capacity: 2);
        subscriber.Flush();
        entries.Clear();

        for (var index = 0; index < 3; index++)
            VisualDiagnostics.OnChildAdded(null, new Label(), index);
        subscriber.Flush();

        Assert.Equal(1, Snapshot(Assert.Single(entries, e => Kind(e) == "capture.dropped"))["count"]!.GetValue<int>());
        Assert.Equal([1, 2], entries.Where(e => Kind(e) == "visual-tree.added")
            .Select(e => Snapshot(e)["childIndex"]!.GetValue<int>()).ToArray());
        Assert.Equal([3L, 4L], entries.Where(e => Kind(e) == "visual-tree.added")
            .Select(e => Snapshot(e)["sequence"]!.GetValue<long>()).ToArray());
    }

    [Fact]
    public void QueuedSnapshots_DoNotRetainViews()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);
        var weak = AddUnretainedView();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
        subscriber.Flush();
        Assert.Contains(entries, e => Kind(e) == "visual-tree.added");
    }

    [Fact]
    public void OptionalHotReload_RecordsFailuresBeforeTerminalApplyAndUnsubscribes()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = new MauiDiagnosticsSubscriber(entries.Add, startTimer: false, hotReloadType: typeof(HotReloadHub));
        Assert.Equal(4, HotReloadHub.SubscriberCount);

        HotReloadHub.Request();
        HotReloadHub.Fail();
        HotReloadHub.Apply();
        HotReloadHub.Skip();
        subscriber.Flush();

        Assert.Equal(["hot-reload.requested", "hot-reload.failed", "hot-reload.applied", "hot-reload.skipped"],
            entries.Select(Kind).Where(k => k.StartsWith("hot-reload.", StringComparison.Ordinal)).ToArray());
        var failed = Snapshot(Assert.Single(entries, e => Kind(e) == "hot-reload.failed"));
        var applied = Snapshot(Assert.Single(entries, e => Kind(e) == "hot-reload.applied"));
        Assert.Equal(failed["version"]!.GetValue<int>(), applied["toVersion"]!.GetValue<int>());
        Assert.Equal(25, applied["durationMs"]!.GetValue<double>());
        Assert.DoesNotContain("private-exception-message", entries.Single(e => Kind(e) == "hot-reload.failed").Message);

        subscriber.Dispose();
        Assert.Equal(0, HotReloadHub.SubscriberCount);
        var count = entries.Count;
        HotReloadHub.Request();
        VisualDiagnostics.OnChildAdded(null, new Label(), 0);
        subscriber.Flush();
        Assert.Equal(count, entries.Count);
    }

    [Fact]
    public void MissingHotReloadApi_LeavesOtherStreamsAvailable()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);
        VisualDiagnostics.OnChildAdded(null, new Label(), 0);
        subscriber.Flush();

        Assert.DoesNotContain(subscriber.CapturedEvents, kind => kind.StartsWith("hot-reload.", StringComparison.Ordinal));
        Assert.Contains(entries, e => Kind(e) == "visual-tree.added");
    }

    [Fact]
    public void ThrowingSink_DoesNotEscapeFlushOrDispose()
    {
        var subscriber = new MauiDiagnosticsSubscriber(_ => throw new IOException("disconnected"),
            startTimer: false, hotReloadType: typeof(object));
        VisualDiagnostics.OnChildAdded(null, new Label(), 0);
        subscriber.Flush();
        subscriber.Dispose();
    }

    [Fact]
    public void SnapshotStrings_AreBounded()
    {
        var entries = new List<FileLogEntry>();
        using var subscriber = Create(entries);
        VisualDiagnostics.OnChildAdded(null, new Label { AutomationId = new string('a', 10_000) }, 0);
        subscriber.Flush();

        var added = Snapshot(Assert.Single(entries, e => Kind(e) == "visual-tree.added"));
        Assert.Equal(1024, added["child"]!["automationId"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task Timer_DeliversQueuedEventsToLogSink()
    {
        var delivered = new TaskCompletionSource<FileLogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscriber = new MauiDiagnosticsSubscriber(entry =>
        {
            if (Kind(entry) == "visual-tree.added")
                delivered.TrySetResult(entry);
        }, hotReloadType: typeof(object));
        VisualDiagnostics.OnChildAdded(null, new Label { AutomationId = "LiveEvent" }, 0);

        var entry = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("LiveEvent", Snapshot(entry)["child"]!["automationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Service_RestartReattachesCaptureWithoutDuplicateSubscriptions()
    {
        var provider = new FileLogProvider(_logDir);
        using var service = new TestAgent(new AgentOptions { Port = 0, CaptureMauiDiagnostics = true });
        service.SetLogProvider(provider);
        await service.StopAsync();

        service.StartServerOnly((IAgentDispatcher?)null);
        VisualDiagnostics.OnChildAdded(null, new Label { AutomationId = "AfterRestart" }, 0);
        await service.StopAsync();

        var entries = provider.Reader.Read(100, source: "maui-diagnostics");
        Assert.Single(entries, e => Kind(e) == "visual-tree.added" && e.Message.Contains("AfterRestart"));
        Assert.Equal(2, entries.Count(e => Kind(e) == "capture.started"));
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public async Task Service_RespectsStartupOptionsAndFlushesToExistingLogs(
        bool capture, bool fileLogging, bool agentEnabled, bool expectedCapture)
    {
        var provider = new FileLogProvider(_logDir);
        using var service = new TestAgent(new AgentOptions
        {
            CaptureMauiDiagnostics = capture,
            EnableFileLogging = fileLogging,
            Enabled = agentEnabled
        });
        service.SetLogProvider(provider);

        var capability = Assert.IsType<Dictionary<string, object?>>(service.Capabilities()["maui.diagnostics"]);
        Assert.Equal(expectedCapture, capability["supported"]);
        VisualDiagnostics.OnChildAdded(null, new Label { AutomationId = "StartupChild" }, 0);
        await service.StopAsync();

        var entries = provider.Reader.Read(100, source: "maui-diagnostics");
        Assert.Equal(expectedCapture, entries.Any(e => Kind(e) == "visual-tree.added"));
        Assert.False((bool)((Dictionary<string, object?>)service.Capabilities()["maui.diagnostics"])["supported"]!);
        VisualDiagnostics.OnChildAdded(null, new Label { AutomationId = "AfterStop" }, 0);
        Assert.DoesNotContain(provider.Reader.Read(100, source: "maui-diagnostics"), e => e.Message.Contains("AfterStop"));
    }

    private static MauiDiagnosticsSubscriber Create(List<FileLogEntry> entries, int capacity = 256)
        => new(entries.Add, capacity, startTimer: false, hotReloadType: typeof(object));

    private static JsonObject Snapshot(FileLogEntry entry) => JsonNode.Parse(entry.Message)!.AsObject();
    private static string Kind(FileLogEntry entry) => Snapshot(entry)["kind"]!.GetValue<string>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddUnretainedView()
    {
        var view = new Label();
        VisualDiagnostics.OnChildAdded(null, view, 0);
        return new WeakReference(view);
    }

    private sealed class BindingSource
    {
        public override string ToString() => "private-source-value";
    }

    private sealed class TestAgent(AgentOptions options) : MauiDevFlowAgentService(options)
    {
        internal Dictionary<string, object> Capabilities()
        {
            var capabilities = new Dictionary<string, object>();
            PopulateCapabilities(capabilities);
            return capabilities;
        }
    }

    public static class HotReloadHub
    {
        public static event EventHandler<RequestedArgs>? UpdateRequested;
        public static event EventHandler<AppliedArgs>? UpdateApplied;
        public static event EventHandler<FailedArgs>? UpdateFailed;
        public static event EventHandler<RequestedArgs>? UpdateSkipped;
        public static int SubscriberCount => (UpdateRequested?.GetInvocationList().Length ?? 0)
            + (UpdateApplied?.GetInvocationList().Length ?? 0) + (UpdateFailed?.GetInvocationList().Length ?? 0)
            + (UpdateSkipped?.GetInvocationList().Length ?? 0);
        public static void Request() => UpdateRequested?.Invoke(null, new());
        public static void Fail() => UpdateFailed?.Invoke(null, new());
        public static void Apply() => UpdateApplied?.Invoke(null, new());
        public static void Skip() => UpdateSkipped?.Invoke(null, new());
    }

    public class RequestedArgs : EventArgs
    {
        public IReadOnlyList<Type> UpdatedTypes => [typeof(Label)];
        public IReadOnlyList<Type> HandledTypes => [typeof(Label)];
        public DateTimeOffset Timestamp => DateTimeOffset.UtcNow;
    }

    public sealed class AppliedArgs : RequestedArgs
    {
        public int InstanceCount => 1;
        public int FromVersion => 4;
        public int ToVersion => 5;
        public TimeSpan Duration => TimeSpan.FromMilliseconds(25);
    }

    public sealed class FailedArgs : RequestedArgs
    {
        public Type UpdatedType => typeof(Label);
        public object Instance { get; } = new Label();
        public Exception Exception { get; } = new InvalidOperationException("private-exception-message");
        public int Version => 5;
    }
}
