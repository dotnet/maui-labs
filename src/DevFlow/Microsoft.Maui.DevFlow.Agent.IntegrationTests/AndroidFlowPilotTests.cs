using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Testing;
using Xunit.Abstractions;
using YamlDotNet.RepresentationModel;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Trait("Category", "FlowPilot")]
public sealed class AndroidFlowPilotTests
{
    const int DefaultCleanRepetitions = 3;
    const string PilotCheckpointRoute = AndroidFlowTestHost.DefaultCheckpointRoute;
    const string FragileFlowExclusionReason =
        "The committed flow contains a fragile selector and is not admissible in the required passing corpus.";
    internal const string DemoFlowEnvironmentVariable = "DEVFLOW_FLOW_PILOT_DEMO_FLOW";
    internal const string DemoFlowPrefix = "demo-";
    internal static readonly string[] DeliberateFailureFlowPrefixes = [DemoFlowPrefix, "drifted-"];
    readonly ITestOutputHelper _output;

    public AndroidFlowPilotTests(ITestOutputHelper output)
        => _output = output;

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("2", 2)]
    [InlineData("0", 3)]
    [InlineData("21", 3)]
    [InlineData("not-a-number", 3)]
    public void CleanRepetitions_UsesBoundedConfiguredValue(string? configured, int expected)
        => Assert.Equal(expected, ResolveCleanRepetitions(configured));

    [Fact]
    public async Task TierOneFlows_LoadCommittedPlanBundles()
    {
        var exclusions = new List<(string Path, string Reason)>();
        var flows = await LoadTierOneFlowsAsync(
            AppFixtureBase.FindRepoRoot(),
            (path, reason) => exclusions.Add((path, reason)));

        Assert.All(flows, flow => Assert.NotNull(flow.Plan));
        Assert.All(flows, flow => Assert.Equal(MauiFlowSideEffectPolicies.None, flow.Plan!.SideEffectPolicy));
        Assert.All(flows, flow => Assert.False(ContainsFragileSelector(flow.Flow)));
        Assert.DoesNotContain(
            flows,
            flow => IsDeliberateFailureFlow(flow.SourcePath));
        var exclusion = Assert.Single(exclusions);
        Assert.Equal("recording-2026-08-04T07-33-09.md", exclusion.Path);
        Assert.Equal(FragileFlowExclusionReason, exclusion.Reason);
    }

    [Fact]
    public void SelectTierOneFlowFiles_DefaultSelection_ExcludesDemoFlows()
    {
        var paths = new[]
        {
            Path.Combine("flows", "README.md"),
            Path.Combine("flows", "demo-ci-fix-drift.md"),
            Path.Combine("flows", "drifted-add-todo.md"),
            Path.Combine("flows", "drifted-assert-after-commit.md"),
            Path.Combine("flows", "native-baseline.md"),
            Path.Combine("flows", "verified-add-todo.md"),
        };

        var selected = SelectTierOneFlowFiles(paths, demoFlowSelection: null);

        Assert.Equal(
            ["native-baseline.md", "verified-add-todo.md"],
            selected.Select(static path =>
                Path.GetFileName(path)
                ?? throw new InvalidOperationException("A selected flow path had no file name.")).ToArray());
    }

    [Fact]
    public void SelectTierOneFlowFiles_DemoSelection_LoadsExactlyTheNamedDemoFlow()
    {
        var paths = new[]
        {
            Path.Combine("flows", "demo-ci-fix-drift.md"),
            Path.Combine("flows", "native-baseline.md"),
        };

        var selected = SelectTierOneFlowFiles(paths, "demo-ci-fix-drift.md");

        Assert.Equal("demo-ci-fix-drift.md", Path.GetFileName(Assert.Single(selected)));
    }

    [Theory]
    [InlineData("native-baseline.md")]
    [InlineData("../demo-ci-fix-drift.md")]
    [InlineData("demo-ci-fix-drift")]
    [InlineData("demo-missing.md")]
    public void SelectTierOneFlowFiles_UnsafeOrUnknownDemoSelection_IsRefused(string selection)
    {
        var paths = new[]
        {
            Path.Combine("flows", "demo-ci-fix-drift.md"),
            Path.Combine("flows", "native-baseline.md"),
        };

        Assert.Throws<InvalidOperationException>(() => SelectTierOneFlowFiles(paths, selection));
    }

    [Fact]
    public void TierOneFlowCorpus_ContainsTheCommittedDemoFlowOutsideTheDefaultSelection()
    {
        var directory = Path.Combine(
            AppFixtureBase.FindRepoRoot(), "samples", "DevFlow.Sample", "maui-tests");
        var files = Directory.GetFiles(directory, "*.md");

        Assert.Contains(
            files,
            path => string.Equals(Path.GetFileName(path), "demo-ci-fix-drift.md", StringComparison.Ordinal));
        Assert.Contains(
            files,
            path => string.Equals(Path.GetFileName(path), "drifted-add-todo.md", StringComparison.Ordinal));
        Assert.Contains(
            files,
            path => string.Equals(Path.GetFileName(path), "drifted-assert-after-commit.md", StringComparison.Ordinal));
        Assert.DoesNotContain(
            SelectTierOneFlowFiles(files, demoFlowSelection: null),
            IsDeliberateFailureFlow);
        Assert.Equal(
            "demo-ci-fix-drift.md",
            Path.GetFileName(Assert.Single(SelectTierOneFlowFiles(files, "demo-ci-fix-drift.md"))));
    }

    [AndroidFlowPilotFact]
    public async Task TierOneFlows_RunConfiguredCleanAttempts_AndPublishFirstAttemptManifest()
    {
        var repositoryRoot = AppFixtureBase.FindRepoRoot();
        var configuration = AndroidFlowPilotConfiguration.FromEnvironment(repositoryRoot);
        var manifest = FlowPilotArtifactManifest.Create(configuration.ManifestOptions);
        var failures = new List<string>();
        var flows = new List<FlowPilotFlowSource>();
        AndroidEmulatorFixture? fixture = null;
        AndroidFlowTestHost? host = null;
        var cleanRepetitions = ResolveCleanRepetitions(
            Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_REPEAT"));

        try
        {
            flows = await LoadTierOneFlowsAsync(
                repositoryRoot,
                (path, reason) =>
                {
                    _output.WriteLine($"Android flow-pilot excluded {path}: {reason}");
                    manifest.AddOmission("tier1-flow-excluded", $"{path}: {reason}");
                });
            fixture = new AndroidEmulatorFixture();
            await fixture.InitializeAsync();
            host = fixture.CreateFlowTestHost();

            foreach (var flow in flows)
            {
                for (var repetition = 1; repetition <= cleanRepetitions; repetition++)
                {
                    var runId = $"{flow.Flow.Name}-attempt-{repetition}";
                    var runDirectory = Path.Combine(configuration.ArtifactRoot, runId);
                    Directory.CreateDirectory(runDirectory);

                    // This is a failure-only, redacted CLI evidence adapter. It intentionally
                    // leaves screenshots disabled and is never used to retry a mutation.
                    var evidence = new FlowReplayEvidenceCapture(
                        fixture.Client,
                        Path.Combine(runDirectory, "failure.mauitrace"),
                        repositoryRoot,
                        "android-flow-pilot");

                    var result = await host.RunAsync(
                        flow.Flow,
                        flow.Plan ?? throw new InvalidOperationException(
                            $"{Path.GetFileName(flow.SourcePath)} has no committed plan sidecar."),
                        new AndroidFlowRunRequest
                        {
                            RunId = runId,
                            ArtifactRoot = configuration.ArtifactRoot,
                            FailureEvidenceCapture = evidence,
                            ExpectedCheckpoint = string.IsNullOrWhiteSpace(flow.Plan.Checkpoint?.Route)
                                ? null
                                : new MauiFlowCheckpoint { Route = flow.Plan.Checkpoint!.Route },
                        });

                    manifest.RecordCleanAttempt(flow.Flow, flow.SourcePath, repetition, result.Report);
                    foreach (var artifact in result.Artifacts.Where(static artifact =>
                                 !string.IsNullOrWhiteSpace(artifact.Path)))
                    {
                        manifest.RecordArtifact(artifact);
                    }

                    var failureEvidencePath = Path.Combine(runDirectory, "failure.mauitrace");
                    if (File.Exists(failureEvidencePath))
                    {
                        manifest.RecordArtifact(new MauiFlowArtifactReference
                        {
                            Kind = "mauitrace",
                            Path = failureEvidencePath,
                            Redacted = true,
                            CreatedAt = result.Report.EndedAt ?? DateTimeOffset.UtcNow,
                        });
                    }

                    if (!string.Equals(result.Report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{flow.Flow.Name} clean attempt {repetition}: " +
                            $"{result.Report.Outcome?.Status ?? "unknown"} " +
                            $"({result.Report.Failure?.Code ?? "no-failure-code"}).");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // ADB, emulator, install, reset, launch, and readiness failures are infrastructure
            // facts. Do not turn them into a selector/repair result or silently retry a mutation.
            var diagnostic = AndroidFixtureInitializationDiagnostics.Write(
                configuration.ArtifactRoot,
                ex,
                lifecyclePhase: "android-fixture-initialization");
            if (diagnostic.Artifact is not null)
            {
                manifest.RecordArtifact(diagnostic.Artifact);
                manifest.AddOmission(
                    "fixture-initialization-diagnostic",
                    "A bounded redacted Android fixture initialization diagnostic was captured for qualification accounting.");
            }
            else
            {
                manifest.AddOmission(
                    "fixture-initialization-diagnostic-unavailable",
                    diagnostic.Error ?? "The Android fixture initialization diagnostic was unavailable.");
            }

            foreach (var flow in flows)
            {
                if (!manifest.Flows.Any(entry =>
                        string.Equals(entry.Name, flow.Flow.Name, StringComparison.Ordinal) &&
                        entry.FirstAttempt is not null))
                {
                    manifest.RecordInfrastructureFailure(
                        flow.Flow,
                        flow.SourcePath,
                        repetition: 1,
                        phase: "android-fixture-initialization");
                }
            }

            manifest.AddOmission(
                "android-infrastructure",
                $"The flow pilot stopped before all clean attempts completed ({ex.GetType().Name}).");
            failures.Add($"Android flow-pilot infrastructure failed ({ex.GetType().Name}).");
        }
        finally
        {
            if (host is not null)
            {
                try { await host.DisposeAsync(); }
                catch (Exception ex) { manifest.AddOmission("host-cleanup", $"Host cleanup failed ({ex.GetType().Name})."); }
            }

            if (fixture is not null)
            {
                try { await fixture.DisposeAsync(); }
                catch (Exception ex) { manifest.AddOmission("fixture-cleanup", $"Fixture cleanup failed ({ex.GetType().Name})."); }
            }

            var write = manifest.WriteAtomic();
            if (!write.Ok)
            {
                failures.Add(write.Error ?? "The Android flow-pilot manifest could not be written.");
            }
            else
            {
                _output.WriteLine($"Android flow-pilot manifest: {write.Path}");
                if (write.Validation is { IsValid: false })
                    failures.AddRange(write.Validation.Errors);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    internal static int ResolveCleanRepetitions(string? configured)
        => int.TryParse(configured, out var value) && value is >= 1 and <= 20
            ? value
            : DefaultCleanRepetitions;

    /// <summary>
    /// Selects the flow files the Tier-1 pilot loads. The default is the ordinary Tier-1 corpus and
    /// never includes a deliberately failing fixture: those document repair scenarios and cannot
    /// satisfy a gate that requires every loaded flow to pass. Demo mode is a single explicit
    /// <c>demo-</c>-prefixed file name; it loads that flow and nothing else, so a demo run can never
    /// quietly widen into the ordinary corpus either.
    /// </summary>
    internal static IReadOnlyList<string> SelectTierOneFlowFiles(
        IEnumerable<string> flowPaths,
        string? demoFlowSelection)
    {
        ArgumentNullException.ThrowIfNull(flowPaths);

        var ordered = flowPaths
            .Where(static path => !string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();
        var selection = demoFlowSelection?.Trim();
        if (string.IsNullOrEmpty(selection))
        {
            return ordered
                .Where(static path => !IsDeliberateFailureFlow(path))
                .ToList();
        }

        if (!string.Equals(Path.GetFileName(selection), selection, StringComparison.Ordinal) ||
            !selection.StartsWith(DemoFlowPrefix, StringComparison.Ordinal) ||
            !selection.EndsWith(".md", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DemoFlowEnvironmentVariable} must name a single '{DemoFlowPrefix}' prefixed Markdown flow file, " +
                $"not '{selection}'.");
        }

        var matches = ordered
            .Where(path => string.Equals(Path.GetFileName(path), selection, StringComparison.Ordinal))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"{DemoFlowEnvironmentVariable} selected '{selection}', but {matches.Count} matching flows were found.");
        }

        return matches;
    }

    internal static bool IsDeliberateFailureFlow(string path)
        => DeliberateFailureFlowPrefixes.Any(prefix =>
            Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal));

    internal static bool ContainsFragileSelector(MauiFlow flow)
        => flow.Steps.Any(static step =>
            step.Fragile ||
            FlowSelector.IsFragile(step.Target) ||
            FlowSelector.IsFragile(step.Args?.Selector));

    internal static async Task<List<FlowPilotFlowSource>> LoadTierOneFlowsAsync(
        string repositoryRoot,
        Action<string, string>? reportExclusion = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(repositoryRoot, "samples", "DevFlow.Sample", "maui-tests");
        var loader = new CommittedFlowBundleLoader();
        var sources = new List<FlowPilotFlowSource>();
        var demoSelection = Environment.GetEnvironmentVariable(DemoFlowEnvironmentVariable);
        var demoMode = !string.IsNullOrWhiteSpace(demoSelection);
        foreach (var path in SelectTierOneFlowFiles(Directory.GetFiles(directory, "*.md"), demoSelection))
        {
            var bundle = await loader.LoadAsync(path, planPath: null, cancellationToken);

            // The pilot replays committed flows unattended, so only no-side-effect plans are admissible.
            if (!string.Equals(bundle.Plan.SideEffectPolicy, MauiFlowSideEffectPolicies.None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} declares sideEffectPolicy '{bundle.Plan.SideEffectPolicy}'; " +
                    "the Android flow pilot only replays plans with 'none'.");
            }

            // The harness verifies PilotCheckpointRoute. A committed plan may leave the checkpoint
            // empty (declaring checkpoint requirements makes plain `maui devflow flow run` demand a
            // state-evidence provider), but if it does declare a route it has to be the same one, so
            // a plan and the harness can never disagree silently.
            var declaredRoute = bundle.Plan.Checkpoint?.Route;
            if (!string.IsNullOrWhiteSpace(declaredRoute)
                && !string.Equals(declaredRoute, PilotCheckpointRoute, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} declares checkpoint route '{declaredRoute}', " +
                    $"but the Android flow pilot verifies '{PilotCheckpointRoute}'.");
            }

            if (!demoMode && ContainsFragileSelector(bundle.Flow))
            {
                reportExclusion?.Invoke(Path.GetFileName(path), FragileFlowExclusionReason);
                continue;
            }

            sources.Add(new FlowPilotFlowSource(path, bundle.Flow, bundle.Plan));
        }

        if (demoMode)
        {
            if (sources.Count != 1)
                throw new InvalidOperationException($"Expected exactly 1 demo flow, found {sources.Count}.");
            return sources;
        }

        if (sources.Count is < 6 or > 12)
            throw new InvalidOperationException($"Expected 6-12 Tier-1 flows, found {sources.Count}.");

        return sources;
    }
}

public sealed class FlowPilotArtifactManifestTests
{
    [Fact]
    public void DiagnosticRerun_DoesNotReplaceFirstCleanAttempt()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var flow = Flow();
            var source = SourcePath();
            var first = Report(root, "first", MauiFlowRunOutcomes.Failed, MauiFlowFailureClasses.AssertionFailed);
            var diagnostic = Report(root, "diagnostic", MauiFlowRunOutcomes.Passed, null);

            manifest.RecordCleanAttempt(flow, source, 1, first);
            manifest.RecordDiagnosticRerun(flow, source, diagnostic);

            var entry = Assert.Single(manifest.Flows);
            Assert.Equal(MauiFlowRunOutcomes.Failed, entry.FirstAttempt?.Outcome);
            Assert.Single(entry.DiagnosticReruns);
            Assert.Equal(MauiFlowRunOutcomes.Passed, entry.DiagnosticReruns[0].Outcome);
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void WriteAtomic_BindsRequiredFactsAndHashesArtifacts()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var flow = Flow();
            var source = SourcePath();

            for (var repetition = 1; repetition <= 3; repetition++)
            {
                manifest.RecordCleanAttempt(
                    flow,
                    source,
                    repetition,
                    Report(root, $"attempt-{repetition}", MauiFlowRunOutcomes.Passed, null));
            }

            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            Assert.NotNull(write.Path);
            Assert.True(File.Exists(write.Path));
            Assert.NotNull(write.Validation);
            Assert.True(write.Validation.IsValid, string.Join("; ", write.Validation.Errors));

            using var document = JsonDocument.Parse(File.ReadAllText(write.Path));
            var rootElement = document.RootElement;
            Assert.Equal(1, rootElement.GetProperty("schema").GetInt32());
            Assert.Equal("test-commit", rootElement.GetProperty("repository").GetProperty("commit").GetString());
            Assert.Equal("test-run", rootElement.GetProperty("workflow").GetProperty("runId").GetString());
            Assert.Equal("test-testing-version", rootElement.GetProperty("testing").GetProperty("packageVersion").GetString());
            Assert.Equal("com.companyname.mauitodo", rootElement.GetProperty("app").GetProperty("packageId").GetString());
            Assert.Equal("android", rootElement.GetProperty("platform").GetProperty("name").GetString());
            Assert.Equal("35", rootElement.GetProperty("platform").GetProperty("androidSdk").GetProperty("apiLevel").GetString());
            Assert.Equal("emulator", rootElement.GetProperty("platform").GetProperty("deviceEvidence").GetProperty("kind").GetString());
            Assert.False(rootElement.GetProperty("platform").GetProperty("deviceEvidence").GetProperty("realDevice").GetBoolean());
            Assert.Equal("sha256:apk", rootElement.GetProperty("app").GetProperty("packageDigest").GetString());

            var flowEntry = Assert.Single(rootElement.GetProperty("flows").EnumerateArray().ToArray());
            Assert.Equal(MauiFlowRunOutcomes.Passed, flowEntry.GetProperty("firstAttempt").GetProperty("outcome").GetString());
            Assert.Equal(3, flowEntry.GetProperty("cleanAttempts").GetArrayLength());
            Assert.Equal(
                "android-pm-clear-v1",
                flowEntry.GetProperty("firstAttempt").GetProperty("resetFingerprint").GetString());
            Assert.Equal("seed", flowEntry.GetProperty("firstAttempt").GetProperty("seedFingerprint").GetString());

            var artifacts = rootElement.GetProperty("artifacts").EnumerateArray().ToArray();
            Assert.Equal(3, artifacts.Length);
            var artifact = artifacts[0];
            Assert.StartsWith("sha256:", artifact.GetProperty("sha256").GetString());
            Assert.True(artifact.GetProperty("sizeBytes").GetInt64() > 0);
            Assert.Contains(
                "screenshots",
                rootElement.GetProperty("privacy").GetProperty("excludedByDefault").EnumerateArray()
                    .Select(static item => item.GetString()));
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void WriteAtomic_UsesReportPathWhenSerializedArtifactPathsWereRedacted()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var report = Report(root, "attempt-1", MauiFlowRunOutcomes.Passed, null);
            report.Artifacts.Clear();
            report.Artifacts.Add(new MauiFlowArtifactReference
            {
                Kind = "flow-run-report",
                Redacted = true,
            });

            manifest.RecordCleanAttempt(Flow(), SourcePath(), 1, report);
            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            using var document = JsonDocument.Parse(File.ReadAllText(write.Path!));
            var artifact = Assert.Single(
                document.RootElement.GetProperty("artifacts").EnumerateArray(),
                entry => entry.GetProperty("kind").GetString() == "flow-run-report");
            Assert.EndsWith(
                "attempt-1/flow-run.json",
                artifact.GetProperty("path").GetString(),
                StringComparison.Ordinal);
            Assert.True(artifact.GetProperty("redacted").GetBoolean());
            Assert.DoesNotContain(
                document.RootElement.GetProperty("omissions").EnumerateArray(),
                entry => entry.GetProperty("kind").GetString() == "artifact-path");
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void WriteAtomic_PathlessUnknownArtifactIsReportedAsAnOmission()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var report = Report(root, "attempt-1", MauiFlowRunOutcomes.Passed, null);
            report.Artifacts.Add(new MauiFlowArtifactReference
            {
                Kind = "custom-diagnostic",
                Redacted = true,
            });

            manifest.RecordCleanAttempt(Flow(), SourcePath(), 1, report);
            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            Assert.Contains(
                manifest.Omissions,
                omission => omission.Kind == "artifact-path");
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void WriteAtomic_ArtifactLimitRecordsTruncationAndOmission()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var flow = Flow();
            var source = SourcePath();
            var first = Report(root, "attempt-1", MauiFlowRunOutcomes.Passed, null);
            var extraDirectory = Path.Combine(root, "attempt-1", "diagnostics");
            Directory.CreateDirectory(extraDirectory);
            for (var index = 0; index < 260; index++)
            {
                var path = Path.Combine(extraDirectory, $"fact-{index}.json");
                File.WriteAllText(path, "{}");
                first.Artifacts.Add(new MauiFlowArtifactReference
                {
                    Kind = "host-diagnostics",
                    Path = path,
                    MediaType = "application/json",
                    Redacted = true,
                });
            }

            manifest.RecordCleanAttempt(flow, source, 1, first);
            manifest.RecordCleanAttempt(flow, source, 2, Report(root, "attempt-2", MauiFlowRunOutcomes.Passed, null));
            manifest.RecordCleanAttempt(flow, source, 3, Report(root, "attempt-3", MauiFlowRunOutcomes.Passed, null));

            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            Assert.True(manifest.Truncated);
            Assert.Contains(manifest.Omissions, omission => omission.Kind == "artifact-limit");
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void WriteAtomic_FixtureInitializationDiagnostic_IsHashedAndRecordedForQualification()
    {
        var root = CreateArtifactRoot();
        try
        {
            var manifest = CreateManifest(root);
            var diagnostic = AndroidFixtureInitializationDiagnostics.Write(
                root,
                PlatformFlowLifecycleException.Infrastructure(
                    "Android fixture initialization failed.",
                    new PlatformAdbCommandException(new PlatformFlowLifecycleFailureDetails
                    {
                        LifecyclePhase = "android-device-lifecycle",
                        ActionName = "install Android Debug APK",
                        AdbCommandCategory = "install",
                        ExitCode = 1,
                        TimeoutSeconds = 120,
                        TimedOut = false,
                        CancellationRequested = false,
                        SafeErrorText = "adb protocol fault (couldn't read status length)",
                    })));
            Assert.NotNull(diagnostic.Artifact);

            manifest.RecordInfrastructureFailure(
                Flow(),
                SourcePath(),
                repetition: 1,
                phase: "android-fixture-initialization");
            manifest.RecordArtifact(diagnostic.Artifact!);
            manifest.AddOmission(
                "fixture-initialization-diagnostic",
                "A bounded redacted Android fixture initialization diagnostic was captured for qualification accounting.");

            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            using var document = JsonDocument.Parse(File.ReadAllText(write.Path!));
            var artifact = Assert.Single(
                document.RootElement.GetProperty("artifacts").EnumerateArray(),
                entry => entry.GetProperty("kind").GetString() == "fixture-initialization-diagnostic");
            Assert.StartsWith("sha256:", artifact.GetProperty("sha256").GetString());
            Assert.True(artifact.GetProperty("redacted").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("omissions").EnumerateArray(),
                omission => omission.GetProperty("kind").GetString() == "fixture-initialization-diagnostic");

            var parsed = MauiPreviewQualificationArtifactManifestReader.ParseJson(
                File.ReadAllText(write.Path!));
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
            Assert.Contains(
                parsed.Input.ArtifactRefs,
                reference => reference.Kind == "fixture-initialization-diagnostic" && reference.Redacted == true);
            Assert.Contains(
                parsed.Input.Exclusions,
                exclusion => exclusion.Kind == "android-fixture-initialization");
        }
        finally
        {
            DeleteArtifactRoot(root);
        }
    }

    [Fact]
    public void IntegrationWorkflow_ParsesAndSelectsPilotSeparately()
    {
        var repositoryRoot = AppFixtureBase.FindRepoRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "devflow-integration.yml");
        var workflow = File.ReadAllText(workflowPath);
        var yaml = new YamlStream();
        using (var reader = new StringReader(workflow))
            yaml.Load(reader);

        var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var triggers = RequireMapping(root, "on");
        var dispatch = RequireMapping(triggers, "workflow_dispatch");
        var dispatchInputs = RequireMapping(dispatch, "inputs");
        var dispatchPlatforms = RequireMapping(dispatchInputs, "platforms");
        Assert.Contains(
            "flow-pilot",
            RequireSequence(dispatchPlatforms, "options").Children
                .OfType<YamlScalarNode>()
                .Select(static node => node.Value));
        Assert.IsType<YamlSequenceNode>(RequireNode(triggers, "schedule"));

        var pullRequest = RequireMapping(triggers, "pull_request");
        var types = RequireSequence(pullRequest, "types")
            .Children
            .OfType<YamlScalarNode>()
            .Select(static node => node.Value)
            .ToArray();
        Assert.Contains("labeled", types);
        Assert.Contains("synchronize", types);

        // The pull_request trigger deliberately carries no `paths:` filter. A path-filtered
        // workflow never reports its checks at all, so a required `devflow-flow-gate` would sit at
        // "Expected" forever on every pull request that does not touch DevFlow, and nobody could
        // adopt it in branch protection. The scoping moved into the `plan` job instead, which is
        // how the workflow can always report a conclusion and still refuse to start paid lanes for
        // an unrelated diff. Asserting the filter here would pin the shape that had to be abandoned.
        Assert.DoesNotContain(
            "paths",
            pullRequest.Children.Keys.OfType<YamlScalarNode>().Select(static node => node.Value));

        // Scoping still has to exist somewhere, so pin it where it actually lives.
        Assert.Contains("devflow-paths-changed", workflow, StringComparison.Ordinal);
        Assert.Contains("'src/DevFlow/*'", workflow, StringComparison.Ordinal);
        Assert.Contains("'samples/DevFlow.Sample/*'", workflow, StringComparison.Ordinal);

        var jobs = RequireMapping(root, "jobs");
        Assert.NotNull(RequireNode(jobs, "android-flow-pilot"));
        Assert.Contains("run-android-flow-pilot", workflow, StringComparison.Ordinal);
        Assert.Contains("integration-tests", workflow, StringComparison.Ordinal);
        Assert.Contains("flow-pilot", workflow, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_RUN_ANDROID_FLOW_PILOT", workflow, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/TestResults/devflow-flow/android", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/devflow/", workflow, StringComparison.Ordinal);
        Assert.Contains("Run-DevFlowFlowQa.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("--platform android", workflow, StringComparison.Ordinal);
        Assert.Contains("--repeat 3", workflow, StringComparison.Ordinal);
        Assert.Contains("retention-days: 30", workflow, StringComparison.Ordinal);

        var finalizerPath = Path.Combine(
            repositoryRoot,
            "eng",
            "devflow",
            "Finalize-DevFlowFlowPilotManifest.ps1");
        var finalizer = File.ReadAllText(finalizerPath);
        // The digest and the size are taken from one read of the file, so a file rewritten between
        // passes can never be published with a new hash beside an old length.
        Assert.Contains("Get-FileDigestAndSize", finalizer, StringComparison.Ordinal);
        Assert.Contains("[System.Security.Cryptography.SHA256]::Create()", finalizer, StringComparison.Ordinal);
        Assert.Contains("raw-model-context", finalizer, StringComparison.Ordinal);

        var shellQaPath = Path.Combine(
            repositoryRoot,
            "eng",
            "devflow",
            "Run-DevFlowFlowQa.sh");
        var powerShellQaPath = Path.Combine(
            repositoryRoot,
            "eng",
            "devflow",
            "Run-DevFlowFlowQa.ps1");
        Assert.True(File.Exists(shellQaPath));
        Assert.True(File.Exists(powerShellQaPath));
        var shellQa = File.ReadAllText(shellQaPath);
        Assert.Contains("Category=FlowPilot", shellQa, StringComparison.Ordinal);
        Assert.Contains("Finalize-DevFlowFlowPilotManifest.ps1", shellQa, StringComparison.Ordinal);

        var hostPath = Path.Combine(
            repositoryRoot,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.IntegrationTests",
            "Fixtures",
            "AndroidFlowTestHost.cs");
        var host = File.ReadAllText(hostPath);
        Assert.Contains("NoMutatingRetryScope", host, StringComparison.Ordinal);
        Assert.Contains("RetryMutatingRequests = false", host, StringComparison.Ordinal);
    }

    static FlowPilotArtifactManifest CreateManifest(string artifactRoot)
    {
        var repositoryRoot = AppFixtureBase.FindRepoRoot();
        return FlowPilotArtifactManifest.Create(new FlowPilotManifestOptions
        {
            ArtifactRoot = artifactRoot,
            RepositoryRoot = repositoryRoot,
            Platform = "android",
            PackageId = "com.companyname.mauitodo",
            RepositoryCommit = "test-commit",
            WorkflowRunId = "test-run",
            WorkflowName = "test-workflow",
            WorkflowAttempt = "1",
            TestingPackageVersion = "test-testing-version",
            AndroidApiLevel = "35",
            AndroidAvdName = "devflow-tests-api35",
            AndroidAbi = "x86_64",
            AndroidTarget = "google_apis",
            DotNetWorkloadVersion = "10.0.203",
            DeviceEvidenceKind = "emulator",
            RealDevice = false,
        });
    }

    static MauiFlow Flow() => new()
    {
        Name = "manifest-flow",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Assert,
                Asserts =
                [
                    new FlowAssert
                    {
                        Kind = "exists",
                        Selector = new FlowSelector { AutomationId = "AddButton" },
                        Verify = true,
                    },
                ],
            },
        ],
    };

    static MauiFlowRunReport Report(string artifactRoot, string runId, string outcome, string? failureCode)
    {
        var runDirectory = Path.Combine(artifactRoot, runId);
        Directory.CreateDirectory(runDirectory);
        var reportPath = Path.Combine(runDirectory, "flow-run.json");
        File.WriteAllText(reportPath, $"{{\"runId\":\"{runId}\"}}");

        var report = new MauiFlowRunReport
        {
            RunId = runId,
            FlowDigest = "manifest-flow-digest",
            ReportPath = reportPath,
            ReportDigest = "sha256:manifest-report",
            Outcome = new MauiFlowRunOutcome
            {
                Status = outcome,
                Terminal = true,
                Verified = outcome == MauiFlowRunOutcomes.Passed,
            },
            Target = new MauiFlowRunTarget
            {
                Platform = "android",
                AppId = "com.companyname.mauitodo",
                AppBuildFingerprint = "sha256:apk",
                DeviceId = "emulator-5554",
                DeviceProfile = "1080x1920",
                AgentInstanceId = "agent-1",
            },
            Reset = new MauiFlowResetResult
            {
                ResetIdentity = "android-pm-clear-v1",
                SeedFingerprint = "seed",
                BackendStateFingerprint = "backend",
            },
        };
        if (failureCode is not null)
        {
            report.Failure = new MauiFlowFailure
            {
                Class = failureCode,
                Code = failureCode,
                Phase = "replay",
            };
        }

        report.Artifacts.Add(new MauiFlowArtifactReference
        {
            Kind = "flow-run-report",
            Path = reportPath,
            MediaType = "application/json",
            Redacted = true,
        });
        return report;
    }

    static string SourcePath()
        => Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "samples",
            "DevFlow.Sample",
            "maui-tests",
            "native-baseline.md");

    static string CreateArtifactRoot()
        => Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "flow-pilot-manifest-tests",
            Guid.NewGuid().ToString("N"));

    static void DeleteArtifactRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    static YamlMappingNode RequireMapping(YamlMappingNode parent, string key)
        => Assert.IsType<YamlMappingNode>(RequireNode(parent, key));

    static YamlSequenceNode RequireSequence(YamlMappingNode parent, string key)
        => Assert.IsType<YamlSequenceNode>(RequireNode(parent, key));

    static YamlNode RequireNode(YamlMappingNode parent, string key)
    {
        foreach (var (node, value) in parent.Children)
        {
            if (node is YamlScalarNode { Value: var valueKey } &&
                string.Equals(valueKey, key, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new Xunit.Sdk.XunitException($"Missing YAML key '{key}'.");
    }
}

internal sealed record FlowPilotFlowSource(string SourcePath, MauiFlow Flow, MauiTestPlan? Plan = null);

internal sealed class AndroidFlowPilotConfiguration
{
    AndroidFlowPilotConfiguration(string repositoryRoot)
    {
        RepositoryRoot = repositoryRoot;
        WorkflowRunId = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_WORKFLOW_RUN_ID")
            ?? ResolveWorkflowRunId();
        ArtifactRoot = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT")
            ?? Path.Combine(repositoryRoot, "artifacts", "devflow", WorkflowRunId, "android");
    }

    public string RepositoryRoot { get; }
    public string WorkflowRunId { get; }
    public string ArtifactRoot { get; }

    public FlowPilotManifestOptions ManifestOptions => new()
    {
        ArtifactRoot = ArtifactRoot,
        RepositoryRoot = RepositoryRoot,
        Platform = "android",
        PackageId = "com.companyname.mauitodo",
        RepositoryCommit = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_REPOSITORY_COMMIT"),
        WorkflowRunId = WorkflowRunId,
        WorkflowName = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW"),
        WorkflowAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
        AndroidApiLevel = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_API") ?? "35",
        AndroidAvdName = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_AVD") ?? "devflow-tests-api35",
        AndroidAbi = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64-v8a" : "x86_64",
        AndroidTarget = "google_apis",
        DotNetWorkloadVersion = Environment.GetEnvironmentVariable("DOTNET_WORKLOAD_VERSION"),
        // Android is a shipping, non-experimental MAUI platform. These are statements about the
        // platform backend, not about how strong this lane's device evidence is: the AppKit rule in
        // FlowPilotArtifactManifest reads experimental=true with officialCoverage=false, and that is
        // the distinction they encode. Leaving them unset published a manifest that declined to say
        // what platform it covered, which the downstream failure handoff refuses outright, so the
        // CI failure-to-issue path could not start from the one lane that runs on every pull request.
        Experimental = false,
        OfficialCoverage = true,
        DeviceEvidenceKind = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_DEVICE_EVIDENCE_KIND") ?? "emulator",
        RealDevice = string.Equals(
            Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_REAL_DEVICE"),
            "1",
            StringComparison.Ordinal),
    };

    public static AndroidFlowPilotConfiguration FromEnvironment(string repositoryRoot)
        => new(repositoryRoot);

    static string ResolveWorkflowRunId()
    {
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        var attempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT");
        if (!string.IsNullOrWhiteSpace(runId))
            return string.IsNullOrWhiteSpace(attempt) ? runId : $"{runId}-{attempt}";

        return $"local-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }
}
