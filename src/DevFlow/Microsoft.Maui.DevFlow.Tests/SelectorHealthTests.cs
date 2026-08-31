using System.Text.Json;
using FlowRecordTools = Microsoft.Maui.Cli.DevFlow.Flows.FlowRecordTools;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class SelectorHealthTests
{
    [Fact]
    public void FingerprintBuilder_IsStableAndDoesNotPersistTextOrValue()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var observation = RichObservation();
        var target = new ElementInfo
        {
            Id = "save",
            ParentId = "collection",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            AutomationId = "save-order",
            Text = secret,
            Value = secret,
            IsVisible = true,
            IsEnabled = true,
            Bounds = new BoundsInfo { X = 10, Y = 20, Width = 80, Height = 40 },
            WindowBounds = new BoundsInfo { X = 0, Y = 0, Width = 320, Height = 640 },
            SourceFile = "Views/CheckoutPage.xaml",
            SourceLine = 24,
            SourceHash = "source-a",
            SourceConfidence = "mapped",
        };
        var first = MauiElementFingerprintBuilder.Build(
            MauiSelectorObservationFactory.Create(target, RichTree(target), observation.Context));
        var second = MauiElementFingerprintBuilder.Build(
            MauiSelectorObservationFactory.Create(target, RichTree(target), observation.Context));

        var json = JsonSerializer.Serialize(first, MauiTestingJsonContext.Default.MauiElementFingerprint);

        Assert.Equal(first.FingerprintId, second.FingerprintId);
        Assert.Equal("save-order", first.Managed.AutomationId);
        Assert.NotNull(first.NormalizedBounds);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"value\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidateGenerator_FollowsPriorityAndExposesDeterministicScoreComponents()
    {
        var observation = RichObservation();
        var result = MauiSelectorCandidateGenerator.Generate(observation, new MauiSelectorCandidateGenerationOptions
        {
            LocaleAssumption = "en-US",
            ExactText = "Save",
        });

        Assert.NotNull(result.Fingerprint);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal(
            result.Candidates.OrderBy(candidate => candidate.Priority).Select(candidate => candidate.CandidateId),
            result.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Equal("automation-id", result.Candidates[0].SelectorDescriptor.Kind);
        Assert.Contains(result.Candidates, candidate => candidate.SelectorDescriptor.Kind == "stable-item-key");
        Assert.Contains(result.Candidates, candidate => candidate.SelectorDescriptor.Kind == "native-automation-id");
        Assert.All(result.Candidates, candidate =>
        {
            Assert.Equal(MauiSelectorHealthRules.RankerRuleVersion, candidate.Scores.RuleVersion);
            Assert.Equal(MauiSelectorHealthRules.Uncalibrated, candidate.Calibration.State);
            Assert.True(candidate.Scores.DeterministicRankScore >= 0);
            Assert.Null(candidate.SelectorDescriptor.ExactText);
        });
    }

    [Fact]
    public void CandidateGenerator_RejectsRuntimeAndUnscopedVirtualizedRows()
    {
        var target = new MauiSelectorObservationElement
        {
            Id = "runtime-42",
            Type = "Button",
            Role = "button",
            IsVisible = true,
            IsEnabled = true,
            StableItemKey = "order-42",
            IsVirtualized = true,
        };
        var observation = new MauiSelectorObservation
        {
            Target = target,
            Elements = [target],
            Context = new MauiSelectorObservationContext { Platform = "android", Route = "/orders" },
        };

        var result = MauiSelectorCandidateGenerator.Generate(observation, new MauiSelectorCandidateGenerationOptions
        {
            LocaleAssumption = "en-US",
            ExactText = "Save",
        });

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Omissions, omission => omission.Kind == "stable-item-key");
        Assert.Contains(result.Omissions, omission => omission.Kind == "exact-text");
        Assert.Contains(result.Omissions, omission => omission.Kind == "candidate");
    }

    [Fact]
    public void CandidateGenerator_ExactTextRequiresExplicitLocaleAndUniqueValidation()
    {
        var target = new MauiSelectorObservationElement
        {
            Id = "label",
            Type = "Label",
            IsVisible = true,
            IsEnabled = true,
        };
        var observation = new MauiSelectorObservation
        {
            Target = target,
            Elements = [target],
            Context = new MauiSelectorObservationContext { Locale = "en-US" },
        };

        var result = MauiSelectorCandidateGenerator.Generate(observation, new MauiSelectorCandidateGenerationOptions
        {
            ExactText = "Save",
            LocaleAssumption = "en-US",
            ExactTextMatchCount = 1,
        });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("exact-text", candidate.SelectorDescriptor.Kind);
        Assert.Equal("Save", candidate.SelectorDescriptor.ExactText);
        Assert.Equal("en-US", candidate.ScopeDescriptor.LocaleAssumption);
    }

    [Fact]
    public void Recording_AddsValueFreeEvidenceWithoutReplacingTheActiveSelector()
    {
        var recorder = new FlowRecorder("selector", null, "android", null);

        var result = FlowRecordTools.AddStepCore(
            recorder,
            FlowActions.Tap,
            automationId: "save-order",
            text: null,
            type: null,
            index: null,
            id: null,
            value: null,
            name: null,
            dx: null,
            dy: null,
            itemIndex: null,
            position: null,
            page: "/checkout",
            navigated: false,
            assertsJson: null,
            selectorObservation: RichObservation());

        Assert.True(result.ok, result.error);
        var step = Assert.Single(recorder.Snapshot().Steps);
        Assert.Equal("save-order", step.Target!.AutomationId);
        Assert.NotNull(step.SelectorEvidence?.Fingerprint);
        Assert.NotEmpty(step.SelectorEvidence!.Candidates);
        Assert.DoesNotContain(
            "CorrectHorseBatteryStaple",
            JsonSerializer.Serialize(step.SelectorEvidence, MauiFlowJsonContext.Default.MauiSelectorEvidence),
            StringComparison.Ordinal);
        var parsed = FlowMarkdown.Parse(FlowMarkdown.Serialize(recorder.Finish()));
        Assert.True(parsed.Ok, parsed.Error);
        Assert.NotNull(parsed.Flow!.Steps[0].SelectorEvidence?.Fingerprint);
    }

    [Fact]
    public void Recording_TruncatedObservation_AbstainsFromSelectorCandidates()
    {
        var recorder = new FlowRecorder("selector", null, "android", null);
        var observation = RichObservation();
        observation.Truncated = true;

        var result = FlowRecordTools.AddStepCore(
            recorder,
            FlowActions.Tap,
            automationId: "save-order",
            text: null,
            type: null,
            index: null,
            id: null,
            value: null,
            name: null,
            dx: null,
            dy: null,
            itemIndex: null,
            position: null,
            page: "/checkout",
            navigated: false,
            assertsJson: null,
            selectorObservation: observation);

        Assert.True(result.ok, result.error);
        var evidence = Assert.Single(recorder.Snapshot().Steps).SelectorEvidence;
        Assert.NotNull(evidence?.Fingerprint);
        Assert.Empty(evidence!.Candidates);
        Assert.Contains(evidence.Omissions, omission => omission.Kind == "live-tree");
    }

    [Fact]
    public void Analyzer_DuplicateScopeTemplatesSourceAndAssertions_EmitsStableDiagnostics()
    {
        var flow = new MauiFlow
        {
            Platform = "android",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    StepId = "orders-save",
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector
                        {
                            TypeIndex = new FlowTypeIndex { Type = "Button", Index = 1 },
                        },
                    },
                    SelectorEvidence = new MauiSelectorEvidence
                    {
                        Fingerprint = new MauiElementFingerprint
                        {
                            Context = new MauiElementFingerprintContext { Platform = "android", Route = "/orders" },
                            Managed = new MauiManagedElementIdentity { Type = "Button", Role = "button" },
                            Source = new MauiSourceAnchor { State = "stale", File = "Views/Orders.xaml" },
                            Collection = new MauiCollectionIdentity { Scope = "orders", Virtualized = true, TemplateKind = "DataTemplate" },
                        },
                    },
                },
            ],
        };
        var input = new MauiSelectorHealthAnalysisInput
        {
            Flow = flow,
            Context = new MauiSelectorObservationContext { Platform = "android", Route = "/orders" },
            LiveElements =
            [
                Actionable("save-one", "save"),
                Actionable("save-two", "save"),
                new MauiSelectorObservationElement
                {
                    Id = "hidden-save",
                    Type = "Button",
                    Role = "button",
                    AutomationId = "save",
                    IsVisible = false,
                    IsEnabled = true,
                },
            ],
        };

        var analysis = MauiSelectorHealthAnalyzer.Analyze(input);

        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.DuplicateAutomationId);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.RuntimeIdOrTypeIndex);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.TemplateOrVirtualization);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.SourceAnchor);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.MissingHardPostcondition);
        Assert.All(analysis.Findings, finding => Assert.StartsWith("DFSH", finding.DiagnosticId));
        Assert.Contains(analysis.Findings, finding => finding.StepId == "orders-save");
    }

    [Fact]
    public void Analyzer_PlatformAndPlanCoverage_ReportsMissingDivergentAndHardAssertionGaps()
    {
        var flow = new MauiFlow
        {
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "save" },
                    AcceptanceCriterionIds = ["order-saved"],
                },
            ],
        };
        var plan = new MauiTestPlan
        {
            RequiredPlatforms = ["android", "windows"],
            AcceptanceCriteria =
            [
                new MauiAcceptanceCriterion { CriterionId = "order-saved", Required = true },
            ],
        };
        var input = new MauiSelectorHealthAnalysisInput
        {
            Flow = flow,
            Plan = plan,
            Context = new MauiSelectorObservationContext { Platform = "android" },
            PlatformSnapshots =
            [
                Platform("android", "automation-id"),
                Platform("windows", "native-automation-id"),
            ],
        };

        var analysis = MauiSelectorHealthAnalyzer.Analyze(input);

        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.RequiredPlatform);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.AcceptanceCriterionUncovered);
        Assert.Contains(analysis.Findings, finding => finding.DiagnosticId == MauiSelectorHealthDiagnosticIds.MissingHardPostcondition);
        Assert.Contains(analysis.Coverage, summary => summary.Platform == "android");
    }

    [Fact]
    public async Task Runner_CapturesCandidatesWithoutChangingActiveSelectorOrFallback()
    {
        var driver = new SelectorDriver();
        var flow = new MauiFlow
        {
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "save" } },
                },
            ],
        };

        var report = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunAsync(flow);

        var step = Assert.Single(report.Steps);
        Assert.Equal("save", step.Selector!.AutomationId);
        Assert.NotNull(step.Fingerprint);
        Assert.NotEmpty(step.SelectorCandidates);
        Assert.Equal(1, driver.TapCalls);
        Assert.Equal(MauiFlowRunOutcomes.Passed, report.Outcome!.Status);
    }

    [Fact]
    public void RunReportLimits_CapSelectorCandidatesAndRecordAnOmission()
    {
        var report = new MauiFlowRunReport
        {
            RunId = "selector-cap",
            FlowDigest = new string('a', 64),
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    SelectorCandidates = [Candidate("one", "automation-id"), Candidate("two", "native-automation-id")],
                },
            ],
        };

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits
        {
            MaxSelectorCandidatesPerStep = 1,
        });

        Assert.Single(report.Steps[0].SelectorCandidates);
        Assert.Contains(report.Omissions, omission => omission.Kind == "selector-candidates");
    }

    [Fact]
    public void RunReportSerialization_RedactsOptInExactTextCandidate()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var report = new MauiFlowRunReport
        {
            RunId = "selector-redaction",
            FlowDigest = new string('b', 64),
            Steps =
            [
                new MauiFlowStepAttempt
                {
                    SelectorCandidates =
                    [
                        new MauiSelectorCandidate
                        {
                            CandidateId = "text",
                            Selector = new FlowSelector { Text = secret },
                            SelectorDescriptor = new MauiSelectorCandidateSelector
                            {
                                Kind = "exact-text",
                                ExactText = secret,
                            },
                        },
                    ],
                },
            ],
        };

        var json = System.Text.Encoding.UTF8.GetString(MauiFlowRunReportSerializer.SerializeToUtf8Bytes(report));

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("exactText", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Corpus_ContainsStaticSchemaAndAtLeastQuarterNoRepairCases()
    {
        var root = FindRepositoryRoot();
        var corpus = Path.Combine(root, "tests", "DevFlow", "InspectorCorpus");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "corpus-manifest.json")));
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "schemas", "selector-health-corpus-v1.json")));
        var cases = manifest.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        Assert.Contains(
            schema.RootElement.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
            value => value == "expect");
        Assert.True(cases.Length >= 12);
        Assert.True(cases.Count(item => item.GetProperty("disposition").GetString() == "no-repair") * 4 >= cases.Length);
        foreach (var entry in cases)
        {
            var file = entry.GetProperty("file").GetString()!;
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, file)));
            Assert.Equal(1, fixture.RootElement.GetProperty("schema").GetInt32());
            Assert.True(fixture.RootElement.GetProperty("expect").GetProperty("diagnosticIds").ValueKind == JsonValueKind.Array);
        }
    }

    private static MauiSelectorObservation RichObservation()
    {
        var root = new MauiSelectorObservationElement
        {
            Id = "root",
            Type = "ContentPage",
            AutomationId = "checkout-page",
            IsVisible = true,
            IsEnabled = true,
        };
        var collection = new MauiSelectorObservationElement
        {
            Id = "collection",
            ParentId = "root",
            Type = "CollectionView",
            AutomationId = "orders",
            IsVisible = true,
            IsEnabled = true,
        };
        var target = new MauiSelectorObservationElement
        {
            Id = "save",
            ParentId = "collection",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Role = "button",
            Traits = ["interactive"],
            AutomationId = "save-order",
            NativeAutomationIdentity = "native-save",
            NativeAutomationIdentityKind = "automation-id",
            IsVisible = true,
            IsEnabled = true,
            Bounds = new BoundsInfo { X = 12, Y = 30, Width = 100, Height = 44 },
            WindowBounds = new BoundsInfo { X = 0, Y = 0, Width = 320, Height = 640 },
            SourceFile = "Views/CheckoutPage.xaml",
            SourceLine = 24,
            SourceHash = "source-a",
            SourceConfidence = "mapped",
            StableItemKey = "order-42",
            CollectionScope = "orders",
            IsVirtualized = true,
        };
        return new MauiSelectorObservation
        {
            Target = target,
            Elements = [root, collection, target],
            Context = new MauiSelectorObservationContext
            {
                AppId = "com.example.store",
                AppBuild = "1.0:42",
                Platform = "android",
                Route = "/checkout",
                Window = "window-0",
                Locale = "en-US",
                Theme = "light",
                Orientation = "portrait",
                DisplayProfile = "320x640@2",
                CapabilityVersion = "test-v1",
                ObservedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            },
        };
    }

    private static IReadOnlyList<ElementInfo> RichTree(ElementInfo target) =>
    [
        new ElementInfo
        {
            Id = "root",
            Type = "ContentPage",
            AutomationId = "checkout-page",
            IsVisible = true,
            IsEnabled = true,
            Children =
            [
                new ElementInfo
                {
                    Id = "collection",
                    ParentId = "root",
                    Type = "CollectionView",
                    AutomationId = "orders",
                    IsVisible = true,
                    IsEnabled = true,
                    Children = [target],
                },
            ],
        },
    ];

    private static MauiSelectorObservationElement Actionable(string id, string automationId) => new()
    {
        Id = id,
        Type = "Button",
        Role = "button",
        Traits = ["interactive"],
        AutomationId = automationId,
        IsVisible = true,
        IsEnabled = true,
    };

    private static MauiSelectorHealthPlatformSnapshot Platform(string platform, string kind) => new()
    {
        Platform = platform,
        Candidates = [Candidate(platform + "-" + kind, kind)],
    };

    private static MauiSelectorCandidate Candidate(string id, string kind) => new()
    {
        CandidateId = id,
        SelectorDescriptor = new MauiSelectorCandidateSelector { Kind = kind },
        Validation = new MauiSelectorCandidateValidation { Accepted = true },
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class SelectorDriver : IMauiFlowDriver
    {
        private readonly ElementInfo _root;
        private readonly ElementInfo _save;

        public SelectorDriver()
        {
            _save = new ElementInfo
            {
                Id = "save",
                ParentId = "root",
                Type = "Button",
                Role = "button",
                AutomationId = "save",
                IsVisible = true,
                IsEnabled = true,
                Bounds = new BoundsInfo { X = 10, Y = 10, Width = 100, Height = 40 },
                WindowBounds = new BoundsInfo { X = 0, Y = 0, Width = 320, Height = 640 },
            };
            _root = new ElementInfo
            {
                Id = "root",
                Type = "ContentPage",
                AutomationId = "page",
                IsVisible = true,
                IsEnabled = true,
                Children = [_save],
            };
        }

        public int TapCalls { get; private set; }
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;
        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(
                automationId == "save"
                    ? new List<ElementInfo> { _save }
                    : new List<ElementInfo>());
        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult<ElementInfo?>(id == "save" ? _save : null);
        public Task<bool> TapAsync(string elementId)
        {
            TapCalls++;
            return Task.FromResult(elementId == "save");
        }
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus
        {
            Route = "/checkout",
            Locale = "en-US",
            Theme = "light",
            Orientation = "portrait",
            DisplayProfile = "320x640",
            Agent = new AgentDescriptor { InstanceId = "selector-driver" },
            Device = new DeviceDescriptor { Platform = "android" },
            App = new AppDescriptor { PackageId = "com.example.store", Build = "42", Version = "1.0" },
        });
        public Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0) => Task.FromResult(new List<ElementInfo> { _root });
    }
}
