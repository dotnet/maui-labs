using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class FlowRecordingSpoolStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "flow-spool-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveRestoreAndDelete_PreservesActiveSnapshot()
    {
        var recorder = new FlowRecorder("resume", "Demo", "Android", null);
        recorder.AppendStep(FlowActions.Tap, new FlowSelector { AutomationId = "go" }, null, null, null, false, null);
        var store = new FlowRecordingSpoolStore(_root);
        const string id = "0123456789abcdef01234567";

        store.Save("live-agent", "stable-agent", "session", id, recorder);
        var restored = store.Restore();

        var spool = Assert.Single(restored);
        Assert.Equal("live-agent", spool.AgentId);
        Assert.Equal("stable-agent", spool.StableAgentId);
        Assert.Equal(id, spool.RecordingId);
        Assert.Single(spool.Flow.Steps);
        Assert.True(store.Delete(id));
        Assert.Empty(store.Restore());
    }

    [Fact]
    public void Restore_CorruptSpool_QuarantinesAndWarns()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "0123456789abcdef01234567.json"), "{broken");
        var warnings = new List<string>();

        var restored = new FlowRecordingSpoolStore(_root, warning: warnings.Add).Restore();

        Assert.Empty(restored);
        Assert.NotEmpty(warnings);
        Assert.NotEmpty(Directory.EnumerateFiles(_root, "*.corrupt-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Restore_MismatchedFilenameAndRecordingId_QuarantinesTheSpool()
    {
        var recorder = new FlowRecorder("resume", "Demo", "Windows", null);
        var store = new FlowRecordingSpoolStore(_root);
        const string contentId = "0123456789abcdef01234567";
        const string filenameId = "89abcdef0123456701234567";
        store.Save("agent", "stable", "session", contentId, recorder);
        File.Move(
            Path.Combine(_root, contentId + ".json"),
            Path.Combine(_root, filenameId + ".json"));

        var restored = store.Restore();

        Assert.Empty(restored);
        Assert.NotEmpty(Directory.EnumerateFiles(_root, "*.corrupt-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Restore_ExpiredSpool_DeletesIt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "0123456789abcdef01234567.json"),
            """{"schema":1,"agentId":"agent","recordingId":"0123456789abcdef01234567","createdUtc":"2000-01-01T00:00:00+00:00","lastTouchedUtc":"2000-01-01T00:00:00+00:00","flow":{"schema":1,"name":"old","steps":[]}}""");

        var restored = new FlowRecordingSpoolStore(_root).Restore();

        Assert.Empty(restored);
        Assert.False(File.Exists(Path.Combine(_root, "0123456789abcdef01234567.json")));
    }

    [Fact]
    public void Save_PrunesToTheConfiguredFileCap()
    {
        var store = new FlowRecordingSpoolStore(_root);
        for (var index = 0; index < FlowRecordingSpoolStore.MaxFiles + 3; index++)
        {
            var id = index.ToString("x24");
            var recorder = new FlowRecorder($"flow-{index}", "Demo", "Windows", null);
            store.Save($"agent-{index}", $"stable-{index}", null, id, recorder);
        }

        Assert.True(
            Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly).Count()
            <= FlowRecordingSpoolStore.MaxFiles);
    }

    [Fact]
    public void Coordinator_RejectsRecordingWhenInitialSpoolCannotBePersisted()
    {
        var warnings = new List<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        File.WriteAllText(_root, "not a directory");
        var spools = new FlowRecordingSpoolStore(_root, warning: warnings.Add);
        var recordings = new FlowRecordingStore();
        var coordinator = new BrokerFlowCoordinator(recordings, spools);

        var result = coordinator.Start("agent", "flow", "Demo", "Windows", null);

        Assert.False(result.Ok);
        Assert.False(coordinator.Status("agent").Recording);
        Assert.Contains("durable", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(warnings, warning => warning.Contains("Could not persist", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_RollsBackStepWhenSnapshotExceedsSpoolLimit()
    {
        var spools = new FlowRecordingSpoolStore(_root);
        var recordings = new FlowRecordingStore();
        var coordinator = new BrokerFlowCoordinator(recordings, spools);
        Assert.True(coordinator.Start("agent", "flow", "Demo", "Windows", null).Ok);

        BrokerFlowResult? failure = null;
        for (var index = 0; index < 500; index++)
        {
            var result = coordinator.Observe("agent", new FlowObservation
            {
                Action = FlowActions.Fill,
                AutomationId = $"field-{index}",
                Value = new string('x', 8 * 1024),
            });
            if (!result.Ok)
            {
                failure = result;
                break;
            }
        }

        Assert.NotNull(failure);
        Assert.Contains("durably recorded", failure!.Error!, StringComparison.OrdinalIgnoreCase);
        var status = coordinator.Status("agent");
        Assert.False(status.Ok);
        Assert.True(status.Recording);
        Assert.Equal(failure.Steps, status.Steps);
        Assert.Contains("Cancel this recording", status.Error!, StringComparison.OrdinalIgnoreCase);

        var restarted = new BrokerFlowCoordinator(
            new FlowRecordingStore(),
            new FlowRecordingSpoolStore(_root));
        var restoredStatus = restarted.Status("agent");
        Assert.False(restoredStatus.Ok);
        Assert.True(restoredStatus.Recording);
        Assert.Contains("not durable", restoredStatus.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coordinator_RestartResumesOnlyWithExactRecordingCapability()
    {
        var first = new BrokerFlowCoordinator(
            new FlowRecordingStore(),
            new FlowRecordingSpoolStore(_root));
        var started = first.Start(
            "process-a",
            "resume",
            "Demo",
            "Windows",
            null,
            sessionId: "session",
            stableAgentId: "stable");
        Assert.True(started.Ok, started.Error);
        var observed = first.Observe("process-a", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "First"
        }, started.RecordingId);
        Assert.True(observed.Ok, observed.Error);
        first.RemoveAgent("process-a");

        var restarted = new BrokerFlowCoordinator(
            new FlowRecordingStore(),
            new FlowRecordingSpoolStore(_root));
        Assert.False(restarted.ConnectAgent(
            "process-b",
            "stable",
            "session",
            expectedRecordingId: "wrong-recording-id"));
        Assert.True(restarted.ConnectAgent(
            "process-b",
            "stable",
            "session",
            started.RecordingId));
        Assert.Equal(1, restarted.Status("process-b", started.RecordingId).Steps);
        Assert.True(restarted.Observe("process-b", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "Second"
        }, started.RecordingId).Ok);

        var stopped = restarted.Stop("process-b", started.RecordingId);
        Assert.True(stopped.Ok, stopped.Error);
        var flow = FlowMarkdown.Parse(stopped.Markdown!).Flow!;
        Assert.Equal(2, flow.Steps.Count);
    }

    public void Dispose()
    {
        try { if (File.Exists(_root)) File.Delete(_root); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
