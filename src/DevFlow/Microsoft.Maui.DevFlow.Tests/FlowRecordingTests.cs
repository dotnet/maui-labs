using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Testing;
using FlowRecordTools = Microsoft.Maui.Cli.DevFlow.Flows.FlowRecordTools;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies workflow recording: the pure recorder + session store, the record MCP tools
/// (selector canonicalization, per-step validation gate, path hardening), and — end to end — that a
/// recorded flow serializes to a .md that parses back and validates for replay (replay-compatible by
/// construction). Recording state is a process-wide singleton, so this class resets it around tests.
/// </summary>
public class FlowRecordingTests : System.IDisposable
{
    private readonly List<string> _temporaryRoots = [];

    public FlowRecordingTests() => FlowRecordingStore.Instance.Reset();
    public void Dispose()
    {
        FlowRecordingStore.Instance.Reset();
        foreach (var root in _temporaryRoots)
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // ── FlowRecorder ──

    [Fact]
    public void Recorder_AppendStep_AssignsSequentialSeq()
    {
        var r = new FlowRecorder("s", "App", "Android", null);
        Assert.Equal(1, r.AppendStep("tap", Sel("a"), null, null, null, false, null));
        Assert.Equal(2, r.AppendStep("tap", Sel("b"), null, null, null, false, null));
        Assert.Equal(2, r.StepCount);
        var steps = r.Snapshot().Steps;
        Assert.Equal("step-0001", steps[0].StepId);
        Assert.Equal("step-0002", steps[1].StepId);
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
        Assert.Null(request.MutationTargetMatchCount);
        Assert.Null(request.MutationTargetQuality);
        Assert.Null(request.MutationTargetFragilityReasons);
    }

    [Fact]
    public async Task CaptureMutationTarget_PreservesTextSelectorAfterTargetDisappears()
    {
        var request = new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/tap",
            Body = """{"elementId":"gone"}"""
        };
        var target = new ElementInfo
        {
            Id = "gone",
            Type = "Label",
            Text = "Open details"
        };
        DevFlowAgentService.CaptureMutationTarget(
            request,
            new Label { Text = target.Text },
            target,
            [target]);
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });

        var observation = await service.CreateMutationObservationAsync(request);

        Assert.NotNull(observation);
        Assert.Equal("Open details", observation.Text);
        Assert.Equal("text", observation.Quality);
        Assert.Equal(1, observation.MatchCount);
        Assert.Null(observation.Id);
    }

    [Fact]
    public async Task CaptureMutationTarget_PreservesPasswordSensitivityAfterFillRemovesTarget()
    {
        var request = new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/fill",
            Body = """{"elementId":"gone","text":"correct horse battery staple"}"""
        };
        var target = new ElementInfo
        {
            Id = "gone",
            Type = "Entry",
            Text = "previous value"
        };
        DevFlowAgentService.CaptureMutationTarget(
            request,
            new Entry { IsPassword = true },
            target,
            [target],
            allowTextSelector: false);
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });

        var observation = await service.CreateMutationObservationAsync(request);

        Assert.NotNull(observation);
        Assert.True(observation.Sensitive);
        Assert.Null(observation.Value);
        Assert.Null(observation.Text);
        Assert.Equal("Entry", observation.Type);
        Assert.Equal(0, observation.Index);
        Assert.Null(observation.Id);
    }

    [Fact]
    public async Task CaptureMutationTarget_PreservesSelectorAfterSetPropertyRemovesTarget()
    {
        var request = new HttpRequest
        {
            Method = "PUT",
            Path = "/api/v1/ui/elements/gone/properties/Text",
            Body = """{"value":"updated"}"""
        };
        request.RouteParams["id"] = "gone";
        request.RouteParams["name"] = "Text";
        DevFlowAgentService.CaptureMutationTarget(
            request,
            new Label { AutomationId = "StatusLabel" });
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });

        var observation = await service.CreateMutationObservationAsync(request);

        Assert.NotNull(observation);
        Assert.Equal("setProperty", observation.Action);
        Assert.Equal("StatusLabel", observation.AutomationId);
        Assert.Equal("Text", observation.Name);
        Assert.Equal("updated", observation.Value);
        Assert.Null(observation.Id);
    }

    [Fact]
    public async Task CaptureNativeMutationTarget_ResolvesDialogIdThroughWalker()
    {
        using var service = new NativeCaptureTestService(new ElementInfo
        {
            Id = "native:hwnd:0x1234:dialog:0:automation:Confirm",
            Type = "Button",
            AutomationId = "Confirm"
        });
        var request = new HttpRequest();

        await service.CaptureNativeMutationTargetBeforeMutationAsync(
            request,
            "native:hwnd:0x1234:dialog:0:automation:Confirm");

        Assert.Equal("native:hwnd:0x1234:dialog:0:automation:Confirm", service.Walker.RequestedId);
        Assert.Equal(0, service.Walker.NativeWalkCount);
        Assert.Equal("Confirm", request.MutationTargetAutomationId);
        Assert.Equal("fragile", request.MutationTargetQuality);
        Assert.Contains("uniqueness", Assert.Single(request.MutationTargetFragilityReasons!));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CaptureNativeMutationTarget_UsesNativePasswordState(bool isPassword, bool expectedSensitive)
    {
        var request = new HttpRequest();
        var target = new ElementInfo
        {
            Id = "native:entry",
            Type = "Edit",
            AutomationId = "NativeEntry",
            NativeProperties = new Dictionary<string, string?>
            {
                ["isPassword"] = isPassword.ToString()
            }
        };

        DevFlowAgentService.CaptureNativeMutationTarget(
            request,
            target,
            allowTextSelector: false,
            protectPotentialSecret: true);

        Assert.Equal(expectedSensitive, request.MutationTargetSensitive);
    }

    [Fact]
    public void CaptureNativeMutationTarget_ProtectsValueWhenPasswordStateIsUnavailable()
    {
        var request = new HttpRequest();

        DevFlowAgentService.CaptureNativeMutationTarget(
            request,
            targetInfo: null,
            allowTextSelector: false,
            protectPotentialSecret: true);

        Assert.True(request.MutationTargetSensitive);
        Assert.Equal("fragile", request.MutationTargetQuality);
        Assert.Contains("transient", Assert.Single(request.MutationTargetFragilityReasons!));
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

    [Fact]
    public void BuildSelector_ScopedStableItem_OverridesAmbiguousAutomationIdFacts()
    {
        var stableItemKey = "sha256:" + new string('a', 64);
        var selector = FlowRecordTools.BuildSelector(
            "TodoCheckBox",
            null,
            null,
            null,
            null,
            matchCount: 9,
            quality: "ambiguous",
            fragilityReasons: ["duplicate AutomationId"],
            stableItemKey: stableItemKey,
            collectionScope: "TodoList")!;

        Assert.Equal("TodoCheckBox", selector.AutomationId);
        Assert.Equal(stableItemKey, selector.StableItemKey);
        Assert.Equal("TodoList", selector.CollectionScope);
        Assert.Equal(1, selector.MatchCount);
        Assert.Equal("stable-item-key", selector.Quality);
        Assert.False(FlowSelector.IsFragile(selector));
    }

    [Fact]
    public void FlowSelector_NullScopedItemFields_DoNotChangeCanonicalJson()
    {
        var flow = new MauiFlow
        {
            Name = "compat",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "SaveButton" },
                },
            ],
        };

        var markdown = FlowMarkdown.Serialize(flow);

        Assert.DoesNotContain("stableItemKey", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("collectionScope", markdown, StringComparison.Ordinal);
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
        var coordinator = CreateIsolatedCoordinator();
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
        var navigation = coordinator.Observe("agent", new FlowObservation
        {
            Action = "navigate",
            Value = "//mcp",
            Navigated = true
        });
        Assert.True(navigation.Ok, navigation.Error);

        var stopped = coordinator.Stop("agent");
        Assert.True(stopped.Ok, stopped.Error);
        Assert.Equal(3, stopped.Steps);

        var parsed = FlowMarkdown.Parse(stopped.Markdown!);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(["tap", "fill", "navigate"], parsed.Flow!.Steps.Select(step => step.Action));
    }

    [Fact]
    public void BrokerCoordinator_IsolatesConcurrentInstancesOfTheSamePackage()
    {
        var coordinator = CreateIsolatedCoordinator();
        const string stable = "same-package-and-tfm";
        Assert.True(coordinator.Start(
            "process-a", "A", "App", "Windows", null, "session", stable).Ok);
        Assert.True(coordinator.Start(
            "process-b", "B", "App", "Windows", null, "session", stable).Ok);

        Assert.True(coordinator.Observe("process-a", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "OnlyA"
        }).Ok);
        Assert.True(coordinator.Observe("process-b", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "OnlyB"
        }).Ok);

        var a = FlowMarkdown.Parse(coordinator.Stop("process-a").Markdown!).Flow!;
        var b = FlowMarkdown.Parse(coordinator.Stop("process-b").Markdown!).Flow!;
        Assert.Equal("OnlyA", Assert.Single(a.Steps).Target!.AutomationId);
        Assert.Equal("OnlyB", Assert.Single(b.Steps).Target!.AutomationId);
    }

    [Fact]
    public void BrokerCoordinator_AdoptsExactlyOneDisconnectedRebuildOrphan()
    {
        var coordinator = CreateIsolatedCoordinator();
        const string stable = "same-package-and-tfm";
        var started = coordinator.Start(
            "old-process", "resume", "App", "Windows", null, "session", stable);
        Assert.True(started.Ok);
        Assert.True(coordinator.Observe("old-process", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "BeforeRebuild"
        }).Ok);
        coordinator.RemoveAgent("old-process");

        Assert.True(coordinator.ConnectAgent(
            "new-process", stable, "session", started.RecordingId));
        var resumed = coordinator.Status("new-process");

        Assert.True(resumed.Recording);
        Assert.Equal(started.RecordingId, resumed.RecordingId);
        Assert.True(coordinator.Stop("new-process", resumed.RecordingId).Ok);
    }

    [Fact]
    public void BrokerCoordinator_DoesNotAdoptFromAStillConnectedInstance()
    {
        var coordinator = CreateIsolatedCoordinator();
        const string stable = "same-package-and-tfm";
        Assert.True(coordinator.Start(
            "process-a", "A", "App", "Windows", null, "session", stable).Ok);

        var started = coordinator.Status("process-a");
        Assert.False(coordinator.ConnectAgent(
            "process-b", stable, "session", started.RecordingId));
        Assert.False(coordinator.Status("process-b").Recording);
        Assert.True(coordinator.Status("process-a").Recording);
    }

    [Fact]
    public void BrokerCoordinator_DoesNotAdoptDisconnectedRecordingWithoutCapability()
    {
        var coordinator = CreateIsolatedCoordinator();
        const string stable = "same-package-and-tfm";
        var started = coordinator.Start(
            "process-a", "A", "App", "Windows", null, "session", stable);
        Assert.True(started.Ok);
        coordinator.RemoveAgent("process-a");

        Assert.False(coordinator.ConnectAgent("process-c", stable, "session"));
        Assert.False(coordinator.Status("process-c").Recording);
        Assert.True(coordinator.Status("process-a").Recording);
        Assert.False(coordinator.ConnectAgent(
            "process-c", stable, "session", "000000000000000000000000"));
        Assert.True(coordinator.Status("process-a").Recording);
    }

    [Fact]
    public void BrokerCoordinator_RecordingCapabilitySelectsTheIntendedDisconnectedInstance()
    {
        var coordinator = CreateIsolatedCoordinator();
        const string stable = "same-package-and-tfm";
        var a = coordinator.Start(
            "process-a", "A", "App", "Windows", null, "session", stable);
        var b = coordinator.Start(
            "process-b", "B", "App", "Windows", null, "session", stable);
        Assert.True(a.Ok);
        Assert.True(b.Ok);
        coordinator.RemoveAgent("process-a");
        coordinator.RemoveAgent("process-b");

        Assert.True(coordinator.ConnectAgent(
            "process-c", stable, "session", b.RecordingId));
        var resumed = coordinator.Status("process-c", b.RecordingId);

        Assert.True(resumed.Recording);
        Assert.Equal(b.RecordingId, resumed.RecordingId);
        Assert.True(coordinator.Status("process-a", a.RecordingId).Recording);
    }

    [Fact]
    public void BrokerCoordinator_UsesABoundedGateSetAcrossProcessIds()
    {
        var coordinator = CreateIsolatedCoordinator();
        for (var index = 0; index < 500; index++)
            Assert.False(coordinator.Status($"process-{index}").Recording);

        var field = typeof(BrokerFlowCoordinator)
            .GetField(
                "_gates",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var gates = Assert.IsType<object[]>(field!.GetValue(coordinator));
        Assert.Equal(64, gates.Length);
    }

    [Fact]
    public void BrokerCoordinator_PersistsSyntheticAssertObservation()
    {
        var coordinator = CreateIsolatedCoordinator();
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
        var coordinator = CreateIsolatedCoordinator();
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

    [Fact]
    public void SensitiveFill_StoresOnlyAnEnvironmentReference()
    {
        var recorder = new FlowRecorder("login", "App", "Windows", null);
        const string secret = "do-not-persist-this-password";

        var added = FlowRecordTools.AddStepCore(
            recorder,
            FlowActions.Fill,
            automationId: "PasswordEntry",
            text: null,
            type: "Entry",
            index: null,
            id: null,
            value: secret,
            name: null,
            dx: null,
            dy: null,
            itemIndex: null,
            position: null,
            page: null,
            navigated: false,
            assertsJson: null);

        Assert.True(added.ok, added.error);
        var flow = recorder.Snapshot();
        var step = Assert.Single(flow.Steps);
        Assert.Null(step.Value);
        Assert.Null(step.Args!.Text);
        Assert.StartsWith(FlowSecretReference.EnvironmentPrefix, step.Args.SecretEnvironmentVariable);
        Assert.Null(step.Asserts);

        var serialized = FlowMarkdown.Serialize(flow);
        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.Contains(step.Args.SecretEnvironmentVariable!, serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PrivateKeyInput")]
    [InlineData("AccessKey")]
    [InlineData("SigningKey")]
    [InlineData("OneTimeOtp")]
    [InlineData("PaymentCvv")]
    [InlineData("CustomerSsn")]
    [InlineData("MfaCode")]
    public void CommonSensitiveFieldNames_AreDetected(string name)
    {
        Assert.True(FlowSecretReference.LooksSensitive(name));
    }

    [Fact]
    public void SensitiveAssertion_IsRetainedOnlyAsANonVerifyingRedactedNote()
    {
        var recorder = new FlowRecorder("login", "App", "Windows", null);
        const string secret = "assertion-secret";
        var asserts = JsonSerializer.Serialize(new[]
        {
            new FlowAssert
            {
                Kind = "propEquals",
                Selector = new FlowSelector { AutomationId = "PasswordEntry" },
                Name = "Text",
                Expected = secret,
                Verify = true
            }
        });

        var added = FlowRecordTools.AddStepCore(
            recorder, FlowActions.Assert,
            null, null, null, null, null, null, null,
            null, null, null, null, null, false, asserts);

        Assert.True(added.ok, added.error);
        var assertion = Assert.Single(Assert.Single(recorder.Snapshot().Steps).Asserts!);
        Assert.Equal("<redacted>", assertion.Expected);
        Assert.False(assertion.Verify);
        Assert.DoesNotContain(secret, FlowMarkdown.Serialize(recorder.Snapshot()), StringComparison.Ordinal);
    }

    [Fact]
    public void NonTextPropertyOnPasswordControl_RemainsANormalLiteral()
    {
        var recorder = new FlowRecorder("login", "App", "Windows", null);

        var added = FlowRecordTools.AddStepCore(
            recorder,
            FlowActions.SetProperty,
            automationId: "PasswordEntry",
            text: null,
            type: "Entry",
            index: null,
            id: null,
            value: "False",
            name: "IsEnabled",
            dx: null,
            dy: null,
            itemIndex: null,
            position: null,
            page: null,
            navigated: false,
            assertsJson: null);

        Assert.True(added.ok, added.error);
        var step = Assert.Single(recorder.Snapshot().Steps);
        Assert.Equal("False", step.Value);
        Assert.Equal("False", step.Args!.Value);
        Assert.Null(step.Args.SecretEnvironmentVariable);
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
        var coordinator = CreateIsolatedCoordinator();
        var started = coordinator.Start("agent", "scenario", null, null, null);
        Assert.True(started.Ok, started.Error);

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
    public async Task Batch_MutationsAppendEachSuccessfulWorkflowStep()
    {
        var brokerPort = GetFreePort();
        var agentPort = GetFreePort();
        using var broker = new BrokerServer(brokerPort, TimeSpan.FromMinutes(1), requireWorkflowRunAuthorization: false);
        using var brokerCancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(brokerCancellation.Token);
        await WaitForBrokerAsync(brokerPort);

        try
        {
            var registration = new BrokerRegistration(
                "batch-recording",
                "net10.0",
                "test",
                "Batch Recording",
                sessionId: "batch",
                packageId: "batch.recording",
                brokerPort: brokerPort)
            {
                CurrentPort = agentPort
            };
            Assert.Equal(
                agentPort,
                await registration.TryRegisterAsync(TimeSpan.FromSeconds(5)));

            using var service = new DevFlowAgentService(new AgentOptions { Port = agentPort });
            service.SetBrokerRegistration(registration);
            service.StartServerOnly(new ImmediateDispatcher());
            service.BindApp(new RecordingTestApplication(
            [
                new Button { AutomationId = "BatchButton", Text = "Tap" },
                new Entry { AutomationId = "BatchEntry" }
            ]));

            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(
                "127.0.0.1",
                agentPort);
            await WaitForAgentAsync(client);
            var lease = await client.ControlMutationLeaseAsync("claim");
            Assert.True(lease.YouHold);
            var started = await client.ControlMutationRecordingAsync(
                "start",
                "batch",
                "Batch Recording",
                "test");
            Assert.True(started.Ok, started.Error);

            var button = Assert.Single(await client.QueryAsync(automationId: "BatchButton"));
            var entry = Assert.Single(await client.QueryAsync(automationId: "BatchEntry"));
            var result = await client.BatchAsync(
            [
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "tap",
                    ["elementId"] = button.Id
                },
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "fill",
                    ["elementId"] = entry.Id,
                    ["text"] = "hello"
                }
            ]);
            Assert.True(result.GetProperty("success").GetBoolean());

            var status = await client.ControlMutationRecordingAsync(
                "status",
                null,
                null,
                null,
                null,
                recordingId: started.RecordingId);
            Assert.Equal(2, status.Steps);
            var stopped = await client.ControlMutationRecordingAsync(
                "stop",
                null,
                null,
                null,
                null,
                recordingId: started.RecordingId);
            Assert.True(stopped.Ok, stopped.Error);
            var flow = FlowMarkdown.Parse(stopped.Markdown!).Flow!;
            Assert.Equal(
                [FlowActions.Tap, FlowActions.Fill],
                flow.Steps.Select(step => step.Action).ToArray());
        }
        finally
        {
            brokerCancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void LeaseTakeover_PreservesRecording_ForNewOwner()
    {
        var coordinator = CreateIsolatedCoordinator();
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
        var coordinator = CreateIsolatedCoordinator();
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "owner", "web", "Browser", false).YouHold);
        var started = coordinator.Start("agent", "scenario", null, null, null);
        Assert.True(started.Ok, started.Error);

        leases.Control("agent", "release", "owner", "web", "Browser", false);

        Assert.True(coordinator.Status("agent").Recording);
        Assert.True(coordinator.Cancel("agent").Ok);
    }

    [Fact]
    public void StaleRecordingId_CannotStopCancelOrAppendToReplacementRecording()
    {
        var coordinator = CreateIsolatedCoordinator();
        var first = coordinator.Start("agent", "first", null, null, null);
        Assert.True(first.Ok, first.Error);
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
        var coordinator = CreateIsolatedCoordinator();
        var started = coordinator.Start("agent", "scenario", null, null, null);
        Assert.True(started.Ok, started.Error);
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

    private BrokerFlowCoordinator CreateIsolatedCoordinator()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "flow-recording-tests",
            Guid.NewGuid().ToString("N"));
        _temporaryRoots.Add(root);
        return new BrokerFlowCoordinator(
            new FlowRecordingStore(),
            new FlowRecordingSpoolStore(root));
    }

    private static string StartRecording(string name = "s")
    {
        var id = FlowRecordingStore.Instance.Start(name, "App", "Android", null);
        Assert.NotNull(id);
        return id!;
    }

    private static bool Ok(string toolJson) => JsonDocument.Parse(toolJson).RootElement.GetProperty("ok").GetBoolean();

    private static int StepCount(string id)
        => FlowRecordingStore.Instance.TryGet(id, out var r) ? r.StepCount : -1;

    private static int GetFreePort() => TestPorts.Reserve();

    private static async Task WaitForBrokerAsync(int port)
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(
                    $"http://127.0.0.1:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Broker did not start.");
    }

    private static async Task WaitForAgentAsync(
        Microsoft.Maui.DevFlow.Driver.AgentClient client)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (await client.GetStatusAsync() is not null)
                return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Agent did not start.");
    }

    private sealed class NativeCaptureTestService : DevFlowAgentService
    {
        private static ElementInfo? _nextTarget;

        public NativeCaptureTestService(ElementInfo target)
            : base(CreateOptions(target))
        {
        }

        public NativeCaptureWalker Walker { get; private set; } = null!;

        protected override VisualTreeWalker CreateTreeWalker()
        {
            Walker = new NativeCaptureWalker(_nextTarget);
            _nextTarget = null;
            return Walker;
        }

        private static AgentOptions CreateOptions(ElementInfo target)
        {
            _nextTarget = target;
            return new AgentOptions { Enabled = false };
        }
    }

    private sealed class NativeCaptureWalker(ElementInfo? target) : VisualTreeWalker
    {
        public string? RequestedId { get; private set; }
        public int NativeWalkCount { get; private set; }

        public override ElementInfo? GetNativeElementInfoById(string id)
        {
            RequestedId = id;
            return target;
        }

        public override List<ElementInfo> WalkNativeTree(
            IReadOnlyList<IntPtr> knownWindowHandles,
            int maxDepth = 0)
        {
            NativeWalkCount++;
            return [];
        }
    }

    private sealed class RecordingTestApplication(
        IEnumerable<IVisualTreeElement> children) : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children = children.ToArray();

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }

    private sealed class ImmediateDispatcher : Microsoft.Maui.Dispatching.IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public Microsoft.Maui.Dispatching.IDispatcherTimer CreateTimer() =>
            new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : Microsoft.Maui.Dispatching.IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
