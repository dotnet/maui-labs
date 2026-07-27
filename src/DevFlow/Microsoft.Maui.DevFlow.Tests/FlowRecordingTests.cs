using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies workflow recording: the pure recorder + session store, the record MCP tools
/// (selector canonicalization, per-step validation gate, path hardening), and — end to end — that a
/// recorded flow serializes to a .md that parses back and validates for replay (replay-compatible by
/// construction). Recording state is a process-wide singleton, so this class resets it around tests.
/// </summary>
public class FlowRecordingTests : System.IDisposable
{
    public FlowRecordingTests() => FlowRecordingStore.Instance.Reset();
    public void Dispose() => FlowRecordingStore.Instance.Reset();

    // ── FlowRecorder ──

    [Fact]
    public void Recorder_AppendStep_AssignsSequentialSeq()
    {
        var r = new FlowRecorder("s", "App", "Android", null);
        Assert.Equal(1, r.AppendStep("tap", Sel("a"), null, null, null, false, null));
        Assert.Equal(2, r.AppendStep("tap", Sel("b"), null, null, null, false, null));
        Assert.Equal(2, r.StepCount);
    }

    [Theory]
    [InlineData("auto", null, false)]   // AutomationId → durable
    [InlineData(null, "Hello", true)]   // Text-only → fragile
    public void Recorder_FlagsFragile_WhenNoAutomationId(string? automationId, string? text, bool expectedFragile)
    {
        var r = new FlowRecorder("s", null, null, null);
        var target = automationId is not null ? Sel(automationId) : new FlowSelector { Text = text };
        r.AppendStep("tap", target, null, null, null, false, null);
        Assert.Equal(expectedFragile, r.Finish().Steps[0].Fragile);
    }

    [Fact]
    public void Recorder_NoTarget_IsNotFragile()
    {
        var r = new FlowRecorder("s", null, null, null);
        r.AppendStep("back", null, null, null, null, false, null);
        Assert.False(r.Finish().Steps[0].Fragile);
    }

    [Fact]
    public void AddStepCore_AcceptsAssertStep_AndRendersExpectation()
    {
        var r = new FlowRecorder("s", null, null, null);
        var asserts = "[{\"kind\":\"propEquals\",\"selector\":{\"automationId\":\"Counter\"},\"name\":\"Text\",\"expected\":\"5\",\"verify\":true}]";
        var (ok, seq, count, fragile, error) = FlowRecordTools.AddStepCore(
            r, "assert", null, null, null, null, null, null, null, null, null, null, null, null, false, asserts);

        Assert.True(ok, error);
        Assert.Equal(1, count);
        var md = FlowMarkdown.Serialize(r.Finish());
        Assert.Contains("Assert", md);
        Assert.Contains("Expect Text == \"5\"", md);
    }

    [Fact]
    public void Recorder_Finish_ReturnsSnapshotIndependentOfLaterAppends()
    {
        var r = new FlowRecorder("s", null, null, null);
        r.AppendStep("tap", Sel("a"), null, null, null, false, null);
        var snapshot = r.Finish();
        r.AppendStep("tap", Sel("b"), null, null, null, false, null);
        Assert.Single(snapshot.Steps); // snapshot not mutated by the later append
        Assert.False(string.IsNullOrEmpty(snapshot.RecordedAt));
    }

    [Fact]
    public async Task Recorder_ValidateAndFinish_IsAtomicWithConcurrentAppend()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var recorder = new FlowRecorder("s", null, null, null);
            recorder.AppendStep("tap", Sel("valid"), null, null, null, false, null);
            using var barrier = new Barrier(2);

            var finishTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return recorder.ValidateAndFinish();
            });
            var appendTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return recorder.AppendStep(
                    "setProperty",
                    Sel("late"),
                    null,
                    new FlowStepArgs(),
                    null,
                    false,
                    null);
            });

            var finalization = await finishTask;
            var appendedSeq = await appendTask;
            if (finalization.Flow is not null)
            {
                Assert.Equal(-1, appendedSeq);
                Assert.True(FlowValidator.Validate(finalization.Flow).Ok);
            }
            else
            {
                Assert.True(appendedSeq > 0);
                Assert.False(finalization.Validation.Ok);
            }
        }
    }

    [Fact]
    public void CaptureMutationTarget_PreservesAutomationIdAfterTargetDisappears()
    {
        var request = new HttpRequest();
        DevFlowAgentService.CaptureMutationTarget(
            request,
            new Button { AutomationId = "CloseModalButton" });

        Assert.Equal("CloseModalButton", request.MutationTargetAutomationId);
    }

    // ── FlowRecordingStore ──

    [Fact]
    public void Store_StartGetRemove_Lifecycle()
    {
        var id = FlowRecordingStore.Instance.Start("s", "App", "iOS", null);
        Assert.False(string.IsNullOrEmpty(id));
        Assert.True(FlowRecordingStore.Instance.TryGet(id!, out _));
        Assert.NotNull(FlowRecordingStore.Instance.Remove(id!));
        Assert.False(FlowRecordingStore.Instance.TryGet(id!, out _));
    }

    [Fact]
    public void Store_IssuesHighEntropyUniqueIds()
    {
        var a = FlowRecordingStore.Instance.Start("s", null, null, null)!;
        var b = FlowRecordingStore.Instance.Start("s", null, null, null)!;
        Assert.NotEqual(a, b);
        Assert.Equal(24, a.Length); // 12 random bytes as hex
    }

    [Fact]
    public void Store_EnforcesActiveCap()
    {
        for (var i = 0; i < FlowRecordingStore.MaxActive; i++)
            Assert.NotNull(FlowRecordingStore.Instance.Start("s", null, null, null));
        Assert.Null(FlowRecordingStore.Instance.Start("overflow", null, null, null));
    }

    // ── BuildSelector (canonical single form) ──

    [Fact]
    public void BuildSelector_KeepsOnlyHighestPrecedenceForm()
    {
        var s = FlowRecordTools.BuildSelector("auto", "txt", "Button", 2, "raw")!;
        Assert.Equal("auto", s.AutomationId);
        Assert.Null(s.Text);
        Assert.Null(s.TypeIndex);
        Assert.Null(s.Id);

        Assert.Equal("txt", FlowRecordTools.BuildSelector(null, "txt", "Button", 2, "raw")!.Text);
        var ti = FlowRecordTools.BuildSelector(null, null, "Button", 2, "raw")!.TypeIndex!;
        Assert.Equal("Button", ti.Type);
        Assert.Equal(2, ti.Index);
        Assert.Equal("raw", FlowRecordTools.BuildSelector(null, null, null, null, "raw")!.Id);
        Assert.Null(FlowRecordTools.BuildSelector(null, null, null, null, null));
    }

    // ── ResolveOutputPath (path hardening) ──

    [Fact]
    public void ResolveOutputPath_DefaultsUnderMauiTests()
    {
        var path = FlowRecordTools.ResolveOutputPath("My Scenario", null, null, out var err);
        Assert.Null(err);
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("maui-tests", "My Scenario.md"), path!);
    }

    [Theory]
    [InlineData("notes.txt")]                       // wrong extension
    public void ResolveOutputPath_RejectsNonMarkdown(string file)
    {
        Assert.Null(FlowRecordTools.ResolveOutputPath("s", file, null, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void ResolveOutputPath_RejectsTraversalOutsideWorkspace()
    {
        Assert.Null(FlowRecordTools.ResolveOutputPath("s", Path.Combine("..", "..", "escape.md"), null, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void ResolveOutputPath_RejectsAbsolutePathOutsideWorkspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside.md");
        Assert.Null(FlowRecordTools.ResolveOutputPath("s", outside, null, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void ResolveOutputPath_SanitizesNameSeparators()
    {
        var path = FlowRecordTools.ResolveOutputPath("a/b\\c", null, null, out var err);
        Assert.Null(err);
        Assert.EndsWith("a_b_c.md", path!);
    }

    // ── record_step validation gate ──

    [Theory]
    [InlineData("tap")]                 // no selector
    [InlineData("setProperty")]         // no selector / no name
    [InlineData("navigate")]            // no route
    [InlineData("bogus")]               // unknown action
    public async Task RecordStep_RejectsUnusableStep(string action)
    {
        var id = StartRecording();
        var res = await FlowRecordTools.RecordStep(null!, id, action);
        Assert.False(Ok(res));
        Assert.Equal(0, StepCount(id));
    }

    [Fact]
    public async Task RecordStep_UnknownRecordingId_Fails()
    {
        var res = await FlowRecordTools.RecordStep(null!, "does-not-exist", "tap", automationId: "x");
        Assert.False(Ok(res));
    }

    // ── end-to-end: record → .md → parse → validate ──

    [Fact]
    public async Task Record_Roundtrips_To_ReplayableMarkdown()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "maui-tests-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var id = StartRecording("login");
            Assert.True(Ok(await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "loginButton")));
            Assert.True(Ok(await FlowRecordTools.RecordStep(null!, id, "fill", automationId: "username", value: "alice")));
            Assert.True(Ok(await FlowRecordTools.RecordStep(null!, id, "navigate", value: "//home", navigated: true)));

            var stop = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id, directory: dir)).RootElement;
            Assert.True(stop.GetProperty("ok").GetBoolean());
            var file = stop.GetProperty("file").GetString()!;
            Assert.True(File.Exists(file));

            // The recording is gone after a successful stop.
            Assert.False(FlowRecordingStore.Instance.TryGet(id, out _));

            // The written .md parses back and validates for replay.
            var parsed = FlowMarkdown.Parse(File.ReadAllText(file), file);
            Assert.True(parsed.Ok, parsed.Error);
            Assert.Equal(3, parsed.Flow!.Steps.Count);
            Assert.Equal("login", parsed.Flow.Name);
            Assert.Equal("//home", parsed.Flow.Steps[2].Value);
            Assert.True(FlowValidator.Validate(parsed.Flow).Ok);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RecordStop_DoesNotOverwrite_WithoutFlag()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "maui-tests-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var id1 = StartRecording("dup");
            await FlowRecordTools.RecordStep(null!, id1, "tap", automationId: "a");
            var file = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id1, directory: dir))
                .RootElement.GetProperty("file").GetString()!;

            var id2 = StartRecording("dup");
            await FlowRecordTools.RecordStep(null!, id2, "tap", automationId: "b");
            var second = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id2, directory: dir)).RootElement;
            Assert.False(second.GetProperty("ok").GetBoolean()); // would overwrite → refused

            var overwrite = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id2, directory: dir, overwrite: true)).RootElement;
            Assert.True(overwrite.GetProperty("ok").GetBoolean());
            Assert.True(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RecordCancel_DiscardsWithoutWriting()
    {
        var id = StartRecording();
        await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "a");
        Assert.True(Ok(await FlowRecordTools.RecordCancel(null!, id)));
        Assert.False(FlowRecordingStore.Instance.TryGet(id, out _));
    }

    [Fact]
    public void Recorder_AppendAfterFinish_Fails()
    {
        var r = new FlowRecorder("s", null, null, null);
        r.AppendStep("tap", Sel("a"), null, null, null, false, null);
        r.Finish();
        Assert.Equal(-1, r.AppendStep("tap", Sel("b"), null, null, null, false, null)); // closed
    }

    [Fact]
    public async Task RecordStep_RejectsWhitespaceOnlySelector()
    {
        var id = StartRecording();
        var res = await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "   ");
        Assert.False(Ok(res)); // whitespace-only automationId → treated as missing → no selector
        Assert.Equal(0, StepCount(id));
    }

    [Fact]
    public async Task RecordStop_RejectsEmptyRecording()
    {
        var id = StartRecording();
        var res = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id)).RootElement;
        Assert.False(res.GetProperty("ok").GetBoolean());
        Assert.True(FlowRecordingStore.Instance.TryGet(id, out _)); // kept, not written
    }

    [Fact]
    public async Task RecordStatus_GlobalList_OmitsRecordingIds()
    {
        StartRecording("a");
        var status = JsonDocument.Parse(await FlowRecordTools.RecordStatus(null!)).RootElement;
        Assert.Equal(1, status.GetProperty("count").GetInt32());
        foreach (var entry in status.GetProperty("active").EnumerateArray())
            Assert.False(entry.TryGetProperty("recordingId", out _)); // id is a capability, not leaked
    }

    [Fact]
    public async Task Record_WithFenceInValue_WritesParsableFile()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "maui-tests-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var id = StartRecording("fence");
            // A recorded value containing a fake ```json maui-test``` fence must not corrupt the file.
            var evil = "```json maui-test\n{}\n``` gotcha";
            Assert.True(Ok(await FlowRecordTools.RecordStep(null!, id, "fill", automationId: "note", value: evil)));

            var stop = JsonDocument.Parse(await FlowRecordTools.RecordStop(null!, id, directory: dir)).RootElement;
            Assert.True(stop.GetProperty("ok").GetBoolean());
            var file = stop.GetProperty("file").GetString()!;

            var parsed = FlowMarkdown.Parse(File.ReadAllText(file), file);
            Assert.True(parsed.Ok, parsed.Error);                  // exactly one maui-test block
            Assert.Equal(evil, parsed.Flow!.Steps[0].Value);       // value preserved verbatim in JSON
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FlowMarkdown_NeutralizesFenceInProse_RoundTrips()
    {
        var evil = "```json maui-test\n{}\n```";
        var flow = new MauiFlow { Name = evil + " scenario" };
        flow.Steps.Add(new FlowStep
        {
            Seq = 1,
            Action = "fill",
            Target = new FlowSelector { AutomationId = "x" },
            Value = evil,
            Args = new FlowStepArgs { Text = evil },
        });

        var parsed = FlowMarkdown.Parse(FlowMarkdown.Serialize(flow));
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(evil, parsed.Flow!.Steps[0].Value);
    }

    [Fact]
    public async Task RecordStep_AcceptsValidHardAssertion()
    {
        var id = StartRecording();
        var asserts = "[{\"kind\":\"propEquals\",\"verify\":true,\"name\":\"Text\",\"expected\":\"Hi\",\"selector\":{\"automationId\":\"label\"}}]";
        Assert.True(Ok(await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "btn", assertsJson: asserts)));
    }

    [Fact]
    public async Task RecordStep_RejectsWhitespaceOnlyAssertSelector()
    {
        var id = StartRecording();
        // Step target is fine; the hard 'exists' assertion's selector is whitespace-only.
        var asserts = "[{\"kind\":\"exists\",\"verify\":true,\"selector\":{\"automationId\":\"   \"}}]";
        Assert.False(Ok(await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "btn", assertsJson: asserts)));
        Assert.Equal(0, StepCount(id));
    }

    [Fact]
    public async Task RecordStep_RejectsNullAssertEntry()
    {
        var id = StartRecording();
        // Malformed input must produce a clean tool error, not an unhandled exception.
        Assert.False(Ok(await FlowRecordTools.RecordStep(null!, id, "tap", automationId: "btn", assertsJson: "[null]")));
    }

    [Fact]
    public void BrokerCoordinator_CollectsBrowserCanvasAndMcpMutations_InOneRecording()
    {
        var coordinator = new BrokerFlowCoordinator();
        var started = coordinator.Start("agent", "cross-host", "App", "Windows", null);

        Assert.True(started.Ok, started.Error);
        Assert.True(coordinator.Observe("agent", new FlowObservation
        {
            Action = "tap",
            AutomationId = "BrowserButton"
        }).Ok);
        Assert.True(coordinator.Observe("agent", new FlowObservation
        {
            Action = "fill",
            AutomationId = "CanvasEntry",
            Value = "from canvas"
        }).Ok);
        Assert.True(coordinator.Observe("agent", new FlowObservation
        {
            Action = "navigate",
            Value = "//mcp",
            Navigated = true
        }).Ok);

        var stopped = coordinator.Stop("agent");
        Assert.True(stopped.Ok, stopped.Error);
        Assert.Equal(3, stopped.Steps);

        var parsed = FlowMarkdown.Parse(stopped.Markdown!);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(["tap", "fill", "navigate"], parsed.Flow!.Steps.Select(step => step.Action));
    }

    [Fact]
    public void BrokerCoordinator_PersistsSyntheticAssertObservation()
    {
        var coordinator = new BrokerFlowCoordinator();
        Assert.True(coordinator.Start("agent", "assertion", "App", "Windows", null).Ok);
        const string assertsJson =
            "[{\"kind\":\"propEquals\",\"selector\":{\"automationId\":\"TodoDescription\"},\"name\":\"Text\",\"expected\":\"Hello\",\"verify\":true}]";

        var observed = coordinator.Observe("agent", new FlowObservation
        {
            Action = FlowActions.Assert,
            AssertsJson = assertsJson
        });

        Assert.True(observed.Ok, observed.Error);
        Assert.Equal(1, observed.Seq);
        Assert.Equal(1, observed.Steps);

        var stopped = coordinator.Stop("agent");
        Assert.True(stopped.Ok, stopped.Error);
        var parsed = FlowMarkdown.Parse(stopped.Markdown!);
        Assert.True(parsed.Ok, parsed.Error);
        var step = Assert.Single(parsed.Flow!.Steps);
        Assert.Equal(FlowActions.Assert, step.Action);
        var assertion = Assert.Single(step.Asserts!);
        Assert.Equal("propEquals", assertion.Kind);
        Assert.Equal("TodoDescription", assertion.Selector!.AutomationId);
        Assert.Equal("Hello", assertion.Expected);
    }

    [Theory]
    [InlineData(FlowActions.Fill, null, "Text")]
    [InlineData(FlowActions.SetProperty, "Opacity", "Opacity")]
    public void BrokerCoordinator_AutoAssertsSuccessfulValueMutations(
        string action,
        string? propertyName,
        string expectedPropertyName)
    {
        var coordinator = new BrokerFlowCoordinator();
        Assert.True(coordinator.Start("agent", "auto-assert", "App", "Windows", null).Ok);

        var observed = coordinator.Observe("agent", new FlowObservation
        {
            Action = action,
            AutomationId = "Target",
            Name = propertyName,
            Value = "Expected",
        });

        Assert.True(observed.Ok, observed.Error);
        var stopped = coordinator.Stop("agent");
        Assert.True(stopped.Ok, stopped.Error);
        var parsed = FlowMarkdown.Parse(stopped.Markdown!);
        Assert.True(parsed.Ok, parsed.Error);
        var assertion = Assert.Single(Assert.Single(parsed.Flow!.Steps).Asserts!);
        Assert.Equal("propEquals", assertion.Kind);
        Assert.Equal("Target", assertion.Selector!.AutomationId);
        Assert.Equal(expectedPropertyName, assertion.Name);
        Assert.Equal("Expected", assertion.Expected);
        Assert.True(assertion.Verify);
    }

    [Theory]
    [InlineData("True", "true")]
    [InlineData("1", "1.0")]
    [InlineData("#FF000080", "#80FF0000")]
    [InlineData("#FF0000FF", "#FF0000")]
    public void Replay_PropertyComparison_NormalizesRuntimeFormats(string actual, string expected)
        => Assert.True(FlowReplayer.PropertyValuesEqual(actual, expected));

    [Theory]
    [InlineData("true value", "True Value")]
    [InlineData("#00FF00FF", "#FFFF0000")]
    [InlineData("1", "2")]
    public void Replay_PropertyComparison_RemainsStrictForDifferentValues(string actual, string expected)
        => Assert.False(FlowReplayer.PropertyValuesEqual(actual, expected));

    [Fact]
    public void BrokerCoordinator_AllowsCurrentLeaseToContinueExistingRecording()
    {
        var coordinator = new BrokerFlowCoordinator();
        Assert.True(coordinator.Start("agent", "scenario", null, null, null).Ok);

        var observed = coordinator.Observe("agent", new FlowObservation
        {
            Action = "tap",
            AutomationId = "Button"
        });

        Assert.True(observed.Ok, observed.Error);
        Assert.Equal(1, coordinator.Status("agent").Steps);
        Assert.True(coordinator.Cancel("agent").Ok);
    }

    [Fact]
    public void LeaseTakeover_PreservesRecording_ForNewOwner()
    {
        var coordinator = new BrokerFlowCoordinator();
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "owner", "web", "Browser", false).YouHold);
        Assert.True(coordinator.Start("agent", "first", null, null, null).Ok);

        Assert.True(leases.Control("agent", "claim", "next", "mcp", "MCP", true).YouHold);
        Assert.True(coordinator.Status("agent").Recording);
        Assert.Equal("first", coordinator.Start("agent", "second", null, null, null).Name);
        Assert.True(coordinator.Cancel("agent").Ok);
    }

    [Fact]
    public void LeaseRelease_PreservesRecording_ForLaterHandoff()
    {
        var coordinator = new BrokerFlowCoordinator();
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "owner", "web", "Browser", false).YouHold);
        Assert.True(coordinator.Start("agent", "scenario", null, null, null).Ok);

        leases.Control("agent", "release", "owner", "web", "Browser", false);

        Assert.True(coordinator.Status("agent").Recording);
        Assert.True(coordinator.Cancel("agent").Ok);
    }

    [Fact]
    public void StaleRecordingId_CannotStopCancelOrAppendToReplacementRecording()
    {
        var coordinator = new BrokerFlowCoordinator();
        var first = coordinator.Start("agent", "first", null, null, null);
        Assert.True(first.Ok);
        Assert.True(coordinator.Cancel("agent", first.RecordingId).Ok);

        var second = coordinator.Start("agent", "second", null, null, null);
        Assert.True(second.Ok);
        Assert.NotEqual(first.RecordingId, second.RecordingId);

        Assert.False(coordinator.Stop("agent", first.RecordingId).Ok);
        Assert.False(coordinator.Cancel("agent", first.RecordingId).Ok);
        Assert.False(coordinator.Observe("agent", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "OldButton"
        }, first.RecordingId).Ok);

        var status = coordinator.Status("agent");
        Assert.True(status.Recording);
        Assert.Equal(second.RecordingId, status.RecordingId);
        Assert.Equal(0, status.Steps);
        Assert.True(coordinator.Cancel("agent", second.RecordingId).Ok);
    }

    [Fact]
    public void CancelIfEmpty_DoesNotDeleteRecordingThatGainedAStep()
    {
        var coordinator = new BrokerFlowCoordinator();
        var started = coordinator.Start("agent", "scenario", null, null, null);
        Assert.True(started.Ok);
        Assert.True(coordinator.Observe("agent", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "Button"
        }, started.RecordingId).Ok);

        var cancelled = coordinator.CancelIfEmpty("agent", started.RecordingId);

        Assert.False(cancelled.Ok);
        var status = coordinator.Status("agent");
        Assert.True(status.Recording);
        Assert.Equal(1, status.Steps);
        Assert.True(coordinator.Cancel("agent", started.RecordingId).Ok);
    }

    [Fact]
    public void MutationRecordingTracker_RejectedObservation_DoesNotDisableRecording()
    {
        var tracker = new MutationRecordingTracker();
        tracker.Update(new MutationRecordingStatus { Ok = true, Recording = true });

        tracker.Update(new MutationRecordingStatus { Ok = false, Recording = false, Error = "step rejected" });

        Assert.True(tracker.IsActive);
        tracker.Update(new MutationRecordingStatus { Ok = true, Recording = false });
        Assert.False(tracker.IsActive);
    }

    // ── helpers ──

    private static FlowSelector Sel(string automationId) => new() { AutomationId = automationId };

    private static string StartRecording(string name = "s")
    {
        var id = FlowRecordingStore.Instance.Start(name, "App", "Android", null);
        Assert.NotNull(id);
        return id!;
    }

    private static bool Ok(string toolJson) => JsonDocument.Parse(toolJson).RootElement.GetProperty("ok").GetBoolean();

    private static int StepCount(string id)
        => FlowRecordingStore.Instance.TryGet(id, out var r) ? r.StepCount : -1;
}
