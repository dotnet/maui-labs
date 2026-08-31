using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class WorkflowPlanStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "workflow-plan-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveLoad_RoundTripsPlanSidecarAndUnknownFields()
    {
        var store = CreateStore(out var flowName, out var flow);
        var request = new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow), extension: true),
            ExpectedFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
        };

        var saved = store.Save(request);
        var loaded = store.Load(flowName);

        Assert.True(saved.Ok, saved.Error);
        Assert.NotNull(saved.Snapshot?.Plan);
        Assert.Equal(1, saved.Snapshot!.Plan!.Revision);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Contains("\"x-plan-extension\"", loaded.Snapshot!.PlanJson);
        Assert.True(loaded.Snapshot.Plan!.ExtensionData!.ContainsKey("x-plan-extension"));
        Assert.True(File.Exists(Path.Combine(_root, "maui-tests", "login.maui-plan.json")));
    }

    [Fact]
    public void Load_ChangedFlow_PreservesStalePlanDigestWarning()
    {
        var store = CreateStore(out var flowName, out var flow);
        var originalDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var saved = store.Save(new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, originalDigest),
            ExpectedFlowDigest = originalDigest,
        });
        Assert.True(saved.Ok, saved.Error);

        flow.Steps.Add(new FlowStep
        {
            Seq = 2,
            Action = FlowActions.Tap,
            Target = new FlowSelector { AutomationId = "another-button" },
        });
        File.WriteAllText(Path.Combine(_root, "maui-tests", flowName), FlowMarkdown.Serialize(flow));

        var loaded = store.Load(flowName);

        Assert.True(loaded.Ok, loaded.Error);
        Assert.Contains(loaded.Warnings, warning => warning.Contains("older flow digest", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_ChangedRevision_ReturnsExplicitStaleConflict()
    {
        var store = CreateStore(out var flowName, out var flow);
        var first = store.Save(new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow)),
            ExpectedFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
        });
        Assert.True(first.Ok, first.Error);

        var stale = store.Save(new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow)),
            ExpectedPlanRevision = 0,
            ExpectedPlanDigest = "old",
            ExpectedFlowDigest = first.Snapshot!.FlowDigest,
        });

        Assert.False(stale.Ok);
        Assert.True(stale.Stale);
        Assert.True(stale.RequiresOverwriteConfirmation);
        Assert.Equal("stale", stale.Code);
        Assert.Equal(1, store.Load(flowName).Snapshot!.Plan!.Revision);
    }

    [Fact]
    public void Load_TraversalName_IsRejectedBeforeFilesystemAccess()
    {
        var store = CreateStore(out _, out _);

        var result = store.Load("../outside.md");

        Assert.False(result.Ok);
        Assert.Equal("flow-name-invalid", result.Code);
    }

    [Fact]
    public void Commit_InvalidPlan_DoesNotPartiallyReplaceFlow()
    {
        var store = CreateStore(out var flowName, out var original);
        var before = File.ReadAllText(Path.Combine(_root, "maui-tests", flowName));
        var changed = new MauiFlow
        {
            Name = original.Name,
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "new-save" },
                },
            ],
        };

        var result = store.Commit(new WorkflowBundleCommitRequest
        {
            FlowName = flowName,
            Markdown = FlowMarkdown.Serialize(changed),
            PlanJson = "{}",
            ExpectedFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(original),
        });

        Assert.False(result.Ok);
        Assert.Equal("bundle-invalid", result.Code);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "maui-tests", flowName)));
        Assert.False(File.Exists(Path.Combine(_root, "maui-tests", "login.maui-plan.json")));
    }

    [Fact]
    public void Commit_WriteFailure_RestoresOriginalFlow()
    {
        var store = CreateStore(out var flowName, out var original);
        var flowPath = Path.Combine(_root, "maui-tests", flowName);
        var before = File.ReadAllText(flowPath);
        Directory.CreateDirectory(Path.Combine(_root, "maui-tests", "login.maui-plan.json"));
        var changed = new MauiFlow
        {
            Name = original.Name,
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "changed" },
                },
            ],
        };

        var result = store.Commit(new WorkflowBundleCommitRequest
        {
            FlowName = flowName,
            Markdown = FlowMarkdown.Serialize(changed),
            PlanJson = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(changed)),
            ExpectedFlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(original),
        });

        Assert.False(result.Ok);
        Assert.Equal("write-failed", result.Code);
        Assert.Equal(before, File.ReadAllText(flowPath));
    }

    [Fact]
    public void Commit_NewFlow_WritesCanonicalFlowAndBoundPlan()
    {
        Directory.CreateDirectory(_root);
        var store = new WorkflowPlanStore(_root);
        var flow = ValidFlow();
        var flowName = "new-login.md";

        var result = store.Commit(new WorkflowBundleCommitRequest
        {
            FlowName = flowName,
            Markdown = FlowMarkdown.Serialize(flow),
            PlanJson = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow)),
        });

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(_root, "maui-tests", flowName)));
        Assert.True(File.Exists(Path.Combine(_root, "maui-tests", "new-login.maui-plan.json")));
        Assert.Equal(1, result.Snapshot!.Plan!.Revision);
        Assert.Equal(result.Snapshot.FlowDigest, result.Snapshot.Plan.Flow!.Digest);
    }

    [Fact]
    public void ValidateAndDiff_NewFlow_UseEmptyCommitBaseline()
    {
        Directory.CreateDirectory(_root);
        var store = new WorkflowPlanStore(_root);
        var flow = ValidFlow();
        var flowName = "new-review.md";
        var markdown = FlowMarkdown.Serialize(flow);
        var plan = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow));

        var validation = store.Validate(flowName, markdown, plan);
        var diff = store.Diff(flowName, markdown, plan);

        Assert.True(validation.Ok, validation.Error);
        Assert.Empty(validation.Errors);
        Assert.NotNull(validation.Snapshot);
        Assert.True(diff.Ok, diff.Error);
        Assert.Contains($"draft/{flowName}", diff.Diff);
        Assert.Contains(flow.Name, diff.Diff);
        Assert.False(Directory.Exists(Path.Combine(_root, "maui-tests")));
        Assert.False(File.Exists(Path.Combine(_root, "maui-tests", flowName)));
    }

    [Fact]
    public void ValidateAndDiff_NonexistentWorkspaceRoot_UseEmptyCommitBaselineWithoutCreatingDirectory()
    {
        var missingRoot = Path.Combine(_root, "missing-workspace");
        var workflowRoot = Path.Combine(missingRoot, "maui-tests");
        var store = new WorkflowPlanStore(missingRoot);
        var flow = ValidFlow();
        var flowName = "new-review.md";
        var markdown = FlowMarkdown.Serialize(flow);
        var plan = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(flow));

        var validation = store.Validate(flowName, markdown, plan);
        var diff = store.Diff(flowName, markdown, plan);

        Assert.True(validation.Ok, validation.Error);
        Assert.Empty(validation.Errors);
        Assert.NotNull(validation.Snapshot);
        Assert.Equal(flowName, validation.Snapshot!.Name);
        Assert.True(diff.Ok, diff.Error);
        Assert.Contains($"draft/{flowName}", diff.Diff);
        Assert.Contains(flow.Name, diff.Diff);
        Assert.False(Directory.Exists(missingRoot));
        Assert.False(Directory.Exists(workflowRoot));
    }

    [Fact]
    public void Diff_SameDraft_ProducesStableOutput()
    {
        var store = CreateStore(out var flowName, out var original);
        var changed = new MauiFlow
        {
            Name = original.Name,
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "changed" },
                },
            ],
        };
        var plan = PlanJson(flowName, MauiFlowRunReportSerializer.ComputeFlowDigest(changed));

        var first = store.Diff(flowName, FlowMarkdown.Serialize(changed), plan);
        var second = store.Diff(flowName, FlowMarkdown.Serialize(changed), plan);

        Assert.True(first.Ok, first.Error);
        Assert.Equal(first.Diff, second.Diff);
        Assert.Contains("changed", first.Diff);
    }

    [Fact]
    public void Load_ReparsePoint_IsRejected()
    {
        var store = CreateStore(out _, out _);
        var target = Path.Combine(_root, "target.md");
        File.WriteAllText(target, FlowMarkdown.Serialize(ValidFlow()));
        var link = Path.Combine(_root, "maui-tests", "linked.md");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = store.Load("linked.md");

        Assert.False(result.Ok);
        Assert.Contains(result.Code, new[] { "flow-not-found", "flow-invalid", "workspace-unsafe" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Commit_CrashAfterEachTransactionPhase_RecoveryOnLoad_RestoresCanonicalBundle()
    {
        var recorded = CreateExistingCommitScenario("commit-record");
        var expectedEvents = CaptureTransactionEvents(recorded.Root, store =>
        {
            var result = store.Commit(recorded.Request);
            Assert.True(result.Ok, result.Error);
        });

        for (var index = 0; index < expectedEvents.Count; index++)
        {
            var scenario = CreateExistingCommitScenario("commit-" + index.ToString("D2"));
            var crashStore = new WorkflowPlanStore(scenario.Root, CrashAfter(index + 1));
            Assert.Throws<WorkflowPlanStoreSimulatedTerminationException>(() => crashStore.Commit(scenario.Request));

            var recovered = new WorkflowPlanStore(scenario.Root).Load(scenario.FlowName);

            Assert.True(recovered.Ok, $"Crash after {DescribeTransactionEvent(expectedEvents[index])}: {recovered.Error}");
            Assert.NotNull(recovered.Snapshot?.Plan);
            Assert.Contains(recovered.Snapshot!.FlowDigest!, new[] { scenario.OriginalDigest, scenario.ChangedDigest });
            Assert.Equal(recovered.Snapshot.FlowDigest, recovered.Snapshot.Plan!.Flow!.Digest);
            Assert.Equal(scenario.UnrelatedMarkdown, File.ReadAllText(scenario.UnrelatedFlowPath));
            Assert.Equal(scenario.UnrelatedPlanJson, File.ReadAllText(scenario.UnrelatedPlanPath));
            AssertNoTransactionArtifacts(scenario.Root);
        }
    }

    [Fact]
    public void CommitNewFlow_CrashAfterEachTransactionPhase_RecoveryOnList_NeverLeavesHalfWrittenBundle()
    {
        var recorded = CreateNewFlowCommitScenario("new-commit-record");
        var expectedEvents = CaptureTransactionEvents(recorded.Root, store =>
        {
            var result = store.Commit(recorded.Request);
            Assert.True(result.Ok, result.Error);
        });

        for (var index = 0; index < expectedEvents.Count; index++)
        {
            var scenario = CreateNewFlowCommitScenario("new-commit-" + index.ToString("D2"));
            var crashStore = new WorkflowPlanStore(scenario.Root, CrashAfter(index + 1));
            Assert.Throws<WorkflowPlanStoreSimulatedTerminationException>(() => crashStore.Commit(scenario.Request));

            var listed = new WorkflowPlanStore(scenario.Root).List();
            Assert.True(listed.Ok, $"Crash after {DescribeTransactionEvent(expectedEvents[index])}: {listed.Error}");

            var flowExists = File.Exists(scenario.FlowPath);
            var planExists = File.Exists(scenario.PlanPath);
            Assert.Equal(flowExists, planExists);
            if (flowExists)
            {
                var loaded = new WorkflowPlanStore(scenario.Root).Load(scenario.FlowName);
                Assert.True(loaded.Ok, loaded.Error);
                Assert.NotNull(loaded.Snapshot?.Plan);
                Assert.Equal(loaded.Snapshot!.FlowDigest, loaded.Snapshot.Plan!.Flow!.Digest);
                Assert.Contains(listed.Items, item => item.Name == scenario.FlowName && item.HasPlan);
            }
            else
            {
                Assert.DoesNotContain(listed.Items, item => item.Name == scenario.FlowName);
            }

            Assert.Equal(scenario.UnrelatedMarkdown, File.ReadAllText(scenario.UnrelatedFlowPath));
            Assert.Equal(scenario.UnrelatedPlanJson, File.ReadAllText(scenario.UnrelatedPlanPath));
            AssertNoTransactionArtifacts(scenario.Root);
        }
    }

    [Fact]
    public void Save_CrashAfterEachTransactionPhase_RecoveryOnSave_AllowsRetry()
    {
        var recorded = CreateSaveScenario("save-record");
        var expectedEvents = CaptureTransactionEvents(recorded.Root, store =>
        {
            var result = store.Save(recorded.UpdateRequest);
            Assert.True(result.Ok, result.Error);
        });

        for (var index = 0; index < expectedEvents.Count; index++)
        {
            var scenario = CreateSaveScenario("save-" + index.ToString("D2"));
            var crashStore = new WorkflowPlanStore(scenario.Root, CrashAfter(index + 1));
            Assert.Throws<WorkflowPlanStoreSimulatedTerminationException>(() => crashStore.Save(scenario.UpdateRequest));

            var retried = new WorkflowPlanStore(scenario.Root).Save(scenario.RetryRequest);

            Assert.True(retried.Ok, $"Crash after {DescribeTransactionEvent(expectedEvents[index])}: {retried.Error}");
            Assert.Contains("Recovered sign in", retried.Snapshot!.PlanJson);
            Assert.Equal(scenario.OriginalDigest, retried.Snapshot.FlowDigest);
            Assert.Equal(retried.Snapshot.FlowDigest, retried.Snapshot.Plan!.Flow!.Digest);
            Assert.Equal(scenario.UnrelatedMarkdown, File.ReadAllText(scenario.UnrelatedFlowPath));
            Assert.Equal(scenario.UnrelatedPlanJson, File.ReadAllText(scenario.UnrelatedPlanPath));
            AssertNoTransactionArtifacts(scenario.Root);
        }
    }

    [Fact]
    public void AppendRepairHistory_CrashAfterEachTransactionPhase_RecoveryOnList_PreservesAppendability()
    {
        var recorded = CreateHistoryScenario("history-record");
        var expectedEvents = CaptureTransactionEvents(recorded.Root, store =>
        {
            var result = store.AppendRepairHistory(recorded.FirstRequest);
            Assert.True(result.Ok, result.Error);
        });

        for (var index = 0; index < expectedEvents.Count; index++)
        {
            var scenario = CreateHistoryScenario("history-" + index.ToString("D2"));
            var crashStore = new WorkflowPlanStore(scenario.Root, CrashAfter(index + 1));
            Assert.Throws<WorkflowPlanStoreSimulatedTerminationException>(() => crashStore.AppendRepairHistory(scenario.FirstRequest));

            var listed = new WorkflowPlanStore(scenario.Root).List();
            Assert.True(listed.Ok, $"Crash after {DescribeTransactionEvent(expectedEvents[index])}: {listed.Error}");

            var appended = new WorkflowPlanStore(scenario.Root).AppendRepairHistory(scenario.SecondRequest);
            Assert.True(appended.Ok, appended.Error);
            var lines = File.ReadAllLines(appended.HistoryPath!);
            Assert.InRange(lines.Length, 1, 2);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.True(document.RootElement.TryGetProperty("state", out _));
                Assert.True(document.RootElement.TryGetProperty("hash", out _));
            }
            AssertNoTransactionArtifacts(scenario.Root);
        }
    }

    private WorkflowPlanStore CreateStore(out string flowName, out MauiFlow flow)
    {
        Directory.CreateDirectory(Path.Combine(_root, "maui-tests"));
        flowName = "login.md";
        flow = ValidFlow();
        File.WriteAllText(Path.Combine(_root, "maui-tests", flowName), FlowMarkdown.Serialize(flow));
        return new WorkflowPlanStore(_root);
    }

    private static IReadOnlyList<WorkflowPlanStoreTransactionEvent> CaptureTransactionEvents(
        string root,
        Action<WorkflowPlanStore> operation)
    {
        var events = new List<WorkflowPlanStoreTransactionEvent>();
        operation(new WorkflowPlanStore(root, new WorkflowPlanStoreTestHooks
        {
            OnTransactionEvent = evt =>
            {
                if (evt.Phase != WorkflowPlanStoreTransactionPhase.ManifestDeleted)
                    events.Add(evt);
            },
        }));
        return events;
    }

    private static WorkflowPlanStoreTestHooks CrashAfter(int ordinal)
    {
        var current = 0;
        return new WorkflowPlanStoreTestHooks
        {
            OnTransactionEvent = evt =>
            {
                if (evt.Phase == WorkflowPlanStoreTransactionPhase.ManifestDeleted)
                    return;
                current++;
                if (current == ordinal)
                    throw new WorkflowPlanStoreSimulatedTerminationException("Crash after " + DescribeTransactionEvent(evt));
            },
        };
    }

    private static string DescribeTransactionEvent(WorkflowPlanStoreTransactionEvent evt)
        => evt.Path is null ? evt.Phase.ToString() : $"{evt.Phase}:{Path.GetFileName(evt.Path)}";

    private static void AssertNoTransactionArtifacts(string root)
    {
        var workflowRoot = Path.Combine(root, "maui-tests");
        if (!Directory.Exists(workflowRoot))
            return;
        Assert.Empty(Directory.EnumerateFiles(workflowRoot, "*devflow-txn-*", SearchOption.AllDirectories));
        var devFlowRoot = Path.Combine(workflowRoot, ".devflow");
        if (Directory.Exists(devFlowRoot))
        {
            Assert.Empty(Directory.EnumerateFiles(
                devFlowRoot,
                "workflow-plan-store.transaction*",
                SearchOption.TopDirectoryOnly));
        }
    }

    private ExistingCommitScenario CreateExistingCommitScenario(string name)
    {
        var root = Path.Combine(_root, name);
        var workflowRoot = Path.Combine(root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);

        var flowName = "login.md";
        var original = ValidFlow();
        File.WriteAllText(Path.Combine(workflowRoot, flowName), FlowMarkdown.Serialize(original));
        var originalDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(original);
        var bootstrap = new WorkflowPlanStore(root);
        var saved = bootstrap.Save(new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, originalDigest),
            ExpectedFlowDigest = originalDigest,
        });
        Assert.True(saved.Ok, saved.Error);

        var changed = Flow("login", "changed");
        var changedDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(changed);
        var unrelated = Flow("search", "search");
        var unrelatedFlowPath = Path.Combine(workflowRoot, "search.md");
        var unrelatedMarkdown = FlowMarkdown.Serialize(unrelated);
        File.WriteAllText(unrelatedFlowPath, unrelatedMarkdown);
        var unrelatedPlanPath = Path.Combine(workflowRoot, "search.maui-plan.json");
        var unrelatedPlanJson = PlanJson("search.md", MauiFlowRunReportSerializer.ComputeFlowDigest(unrelated), goal: "Search");
        File.WriteAllText(unrelatedPlanPath, unrelatedPlanJson);

        return new ExistingCommitScenario(
            root,
            flowName,
            originalDigest,
            changedDigest,
            unrelatedFlowPath,
            unrelatedMarkdown,
            unrelatedPlanPath,
            unrelatedPlanJson,
            new WorkflowBundleCommitRequest
            {
                FlowName = flowName,
                Markdown = FlowMarkdown.Serialize(changed),
                PlanJson = PlanJson(flowName, changedDigest, goal: "Changed sign in"),
                ExpectedPlanRevision = saved.Snapshot!.Plan!.Revision,
                ExpectedPlanDigest = saved.Snapshot.PlanDigest,
                ExpectedFlowDigest = saved.Snapshot.FlowDigest,
            });
    }

    private NewFlowCommitScenario CreateNewFlowCommitScenario(string name)
    {
        var root = Path.Combine(_root, name);
        var workflowRoot = Path.Combine(root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);

        var unrelated = Flow("search", "search");
        var unrelatedFlowPath = Path.Combine(workflowRoot, "search.md");
        var unrelatedMarkdown = FlowMarkdown.Serialize(unrelated);
        File.WriteAllText(unrelatedFlowPath, unrelatedMarkdown);
        var unrelatedPlanPath = Path.Combine(workflowRoot, "search.maui-plan.json");
        var unrelatedPlanJson = PlanJson("search.md", MauiFlowRunReportSerializer.ComputeFlowDigest(unrelated), goal: "Search");
        File.WriteAllText(unrelatedPlanPath, unrelatedPlanJson);

        var flowName = "new-login.md";
        var flow = ValidFlow();
        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        return new NewFlowCommitScenario(
            root,
            flowName,
            Path.Combine(workflowRoot, flowName),
            Path.Combine(workflowRoot, "new-login.maui-plan.json"),
            unrelatedFlowPath,
            unrelatedMarkdown,
            unrelatedPlanPath,
            unrelatedPlanJson,
            new WorkflowBundleCommitRequest
            {
                FlowName = flowName,
                Markdown = FlowMarkdown.Serialize(flow),
                PlanJson = PlanJson(flowName, digest),
            });
    }

    private SaveScenario CreateSaveScenario(string name)
    {
        var root = Path.Combine(_root, name);
        var workflowRoot = Path.Combine(root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);

        var flowName = "login.md";
        var flow = ValidFlow();
        File.WriteAllText(Path.Combine(workflowRoot, flowName), FlowMarkdown.Serialize(flow));
        var flowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var bootstrap = new WorkflowPlanStore(root);
        var saved = bootstrap.Save(new WorkflowPlanSaveRequest
        {
            FlowName = flowName,
            PlanJson = PlanJson(flowName, flowDigest),
            ExpectedFlowDigest = flowDigest,
        });
        Assert.True(saved.Ok, saved.Error);

        var unrelated = Flow("search", "search");
        var unrelatedFlowPath = Path.Combine(workflowRoot, "search.md");
        var unrelatedMarkdown = FlowMarkdown.Serialize(unrelated);
        File.WriteAllText(unrelatedFlowPath, unrelatedMarkdown);
        var unrelatedPlanPath = Path.Combine(workflowRoot, "search.maui-plan.json");
        var unrelatedPlanJson = PlanJson("search.md", MauiFlowRunReportSerializer.ComputeFlowDigest(unrelated), goal: "Search");
        File.WriteAllText(unrelatedPlanPath, unrelatedPlanJson);

        return new SaveScenario(
            root,
            flowDigest,
            unrelatedFlowPath,
            unrelatedMarkdown,
            unrelatedPlanPath,
            unrelatedPlanJson,
            new WorkflowPlanSaveRequest
            {
                FlowName = flowName,
                PlanJson = PlanJson(flowName, flowDigest, goal: "Updated sign in"),
                ExpectedPlanRevision = saved.Snapshot!.Plan!.Revision,
                ExpectedPlanDigest = saved.Snapshot.PlanDigest,
                ExpectedFlowDigest = saved.Snapshot.FlowDigest,
            },
            new WorkflowPlanSaveRequest
            {
                FlowName = flowName,
                PlanJson = PlanJson(flowName, flowDigest, goal: "Recovered sign in"),
                ExpectedFlowDigest = flowDigest,
                ConfirmOverwrite = true,
            });
    }

    private HistoryScenario CreateHistoryScenario(string name)
    {
        var root = Path.Combine(_root, name);
        var workflowRoot = Path.Combine(root, "maui-tests");
        Directory.CreateDirectory(workflowRoot);

        var flowName = "repair.md";
        var flow = Flow("repair", "old-save");
        File.WriteAllText(Path.Combine(workflowRoot, flowName), FlowMarkdown.Serialize(flow));
        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        var proposal = new MauiFlowRepairProposal
        {
            ProposalId = "proposal-1",
            Revision = 1,
            State = MauiFlowRepairOutcomeStates.Previewed,
            SourceRunId = "run-local",
            SourceStepId = "1",
            SourceFailureCode = "locator-not-found",
            BaseFlow = new MauiFlowReference { Path = flowName, Digest = digest },
            Candidate = new MauiSelectorCandidate
            {
                CandidateId = "candidate-new-save",
                SelectorDescriptor = new MauiSelectorCandidateSelector { Kind = "automation-id", AutomationId = "new-save" },
                Score = .9,
                Scores = new MauiSelectorCandidateScores { DeterministicRankScore = .9 },
                CalibrationStatus = "uncalibrated",
                RiskFlags = ["ambiguity"],
                OriginCodes = ["source-topology"],
            },
            PatchDigest = digest,
            Trust = "current-local-run",
        };

        return new HistoryScenario(
            root,
            new WorkflowRepairHistoryAppendRequest
            {
                Proposal = proposal,
                State = MauiFlowRepairOutcomeStates.Proposed,
                Reviewer = "reviewer",
                ReasonCode = "first",
            },
            new WorkflowRepairHistoryAppendRequest
            {
                Proposal = proposal,
                State = MauiFlowRepairOutcomeStates.Approved,
                Reviewer = "reviewer",
                ReasonCode = "second",
            });
    }

    private static MauiFlow ValidFlow() => Flow("login", "sign-in");

    private static MauiFlow Flow(string name, string automationId) => new()
    {
        Name = name,
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Target = new FlowSelector { AutomationId = automationId },
            },
        ],
    };

    private static string PlanJson(string flowName, string digest, bool extension = false, string goal = "Sign in")
    {
        var plan = new Dictionary<string, object?>
        {
            ["schema"] = 1,
            ["planId"] = "plan-" + Path.GetFileNameWithoutExtension(flowName),
            ["revision"] = 1,
            ["flow"] = new { path = flowName, digest },
            ["goal"] = goal,
            ["scenarios"] = new[] { new { scenarioId = "login", description = "Sign in", acceptanceCriterionIds = new[] { "session" } } },
            ["assumptions"] = Array.Empty<string>(),
            ["preconditions"] = Array.Empty<object>(),
            ["reset"] = new { required = true, strategy = "fixture" },
            ["acceptanceCriteria"] = new[] { new { criterionId = "session", description = "Session exists", required = true } },
            ["requirements"] = new { requiredCapabilities = Array.Empty<object>(), requiredSemantics = Array.Empty<object>() },
            ["sideEffectPolicy"] = "none",
            ["provenance"] = new { actorKind = "human", channel = "inspector" },
        };
        if (extension)
            plan["x-plan-extension"] = new { retained = true };
        return JsonSerializer.Serialize(plan);
    }

    private sealed record ExistingCommitScenario(
        string Root,
        string FlowName,
        string OriginalDigest,
        string ChangedDigest,
        string UnrelatedFlowPath,
        string UnrelatedMarkdown,
        string UnrelatedPlanPath,
        string UnrelatedPlanJson,
        WorkflowBundleCommitRequest Request);

    private sealed record NewFlowCommitScenario(
        string Root,
        string FlowName,
        string FlowPath,
        string PlanPath,
        string UnrelatedFlowPath,
        string UnrelatedMarkdown,
        string UnrelatedPlanPath,
        string UnrelatedPlanJson,
        WorkflowBundleCommitRequest Request);

    private sealed record SaveScenario(
        string Root,
        string OriginalDigest,
        string UnrelatedFlowPath,
        string UnrelatedMarkdown,
        string UnrelatedPlanPath,
        string UnrelatedPlanJson,
        WorkflowPlanSaveRequest UpdateRequest,
        WorkflowPlanSaveRequest RetryRequest);

    private sealed record HistoryScenario(
        string Root,
        WorkflowRepairHistoryAppendRequest FirstRequest,
        WorkflowRepairHistoryAppendRequest SecondRequest);
}
