using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using YamlDotNet.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[CollectionDefinition("MacOSFlowQa", DisableParallelization = true)]
public sealed class MacOSFlowQaCollection;

/// <summary>
/// Experimental native AppKit QA. Runtime tests are intentionally macOS- and opt-in-gated; the
/// static contract tests run on Windows to prevent AppKit from being conflated with Mac Catalyst.
/// </summary>
[Collection("MacOSFlowQa")]
[Trait("Category", "AppKitFlowQa")]
public sealed class MacOSFlowQaTests
{
    const string PackageId = "com.companyname.mauitodo.appkit";
    const int DefaultCleanRepetitions = 3;

    [Fact]
    public void FixtureSelection_UsesOnlyExplicitMacOSKeyAndNeverMacCatalyst()
    {
        Assert.IsType<MacOSFixture>(AppFixtureFactory.CreateForPlatform("macos", isWindows: false));
        Assert.IsType<MacCatalystFixture>(AppFixtureFactory.CreateForPlatform("maccatalyst", isWindows: false));
        Assert.IsType<MacCatalystFixture>(AppFixtureFactory.CreateForPlatform("mac", isWindows: false));

        var factory = ReadRepositoryFile(
            "src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Fixtures/AppFixtureFactory.cs");
        Assert.Contains("\"macos\" => new MacOSFixture()", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mac\" or \"macos\"", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureLifecycle_UsesSafeOwnedProcessContract()
    {
        var source = ReadRepositoryFile(
            "src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Fixtures/MacOSFixture.cs");
        Assert.Contains("DevFlow.Sample.MacOS", source, StringComparison.Ordinal);
        Assert.Contains("net10.0-macos", source, StringComparison.Ordinal);
        Assert.Contains("appkit-safe-test-relaunch-v1", source, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_INTEGRATION_TEST_SEED", source, StringComparison.Ordinal);
        Assert.Contains("StopOwnedProcessAsync", source, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("VerifyCheckpointAsync", source, StringComparison.Ordinal);
        Assert.Contains("CaptureHostDiagnosticsAsync", source, StringComparison.Ordinal);
        Assert.Contains("FindNewCrashReports", source, StringComparison.Ordinal);
        Assert.DoesNotContain("maccatalyst-safe-test-relaunch", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deferred, and deliberately visible as deferred. The AppKit sample is not yet wired for
    /// integration testing: it declares no dedicated bundle identifier, no
    /// <c>DEVFLOW_INTEGRATION_TEST</c> build configuration, and no integration-test state file, so
    /// the fixture cannot drive a seeded, isolated AppKit app. This states the contract the sample
    /// owes instead of deleting the expectation. It is outside the Goal 4 MVP and must not be
    /// claimed as covered until this test runs green.
    /// </summary>
    [Fact(Skip = "Deferred beyond the Goal 4 MVP: the AppKit sample does not yet declare its dedicated integration-test bundle and seed contract. Not covered; do not claim it.")]
    public void AppKitSample_DeclaresItsDedicatedIntegrationTestBundleAndSeedContract()
    {
        var project = ReadRepositoryFile("samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj");
        Assert.Contains("com.companyname.mauitodo.appkit", project, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_INTEGRATION_TEST", project, StringComparison.Ordinal);
        Assert.Contains("IntegrationTestState.cs", project, StringComparison.Ordinal);
        var program = ReadRepositoryFile("samples/DevFlow.Sample.MacOS/MauiProgram.cs");
        Assert.Contains("com.example.devflow.integrationtest", program, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_INTEGRATION_TEST_SEED", program, StringComparison.Ordinal);
        Assert.Contains("ConfigureIntegrationTestExtension", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityMatrix_StatesConditionalAndUnsupportedAppKitSurfacesExplicitly()
    {
        Assert.Equal(MacOSFlowCapabilities.PlatformKey, "macos");
        Assert.Equal(MacOSFlowCapabilities.Backend, "appkit");
        Assert.Contains(MacOSFlowCapabilities.Matrix, item =>
            item.Name == "webViewContext" && item.State == "conditional");
        Assert.Contains(MacOSFlowCapabilities.Matrix, item =>
            item.Name == "nativeSystemDialogs" && item.State == "unsupported");
        Assert.Contains(MacOSFlowCapabilities.Matrix, item =>
            item.Name == "multiWindowAutomation" && item.State == "unsupported");

        var report = MacOSFlowCapabilities.Evaluate(new AgentStatus
        {
            Capabilities = new AgentCapabilities { WebView = false },
        });
        var webView = Assert.Single(report, static item => item.Name == "webViewContext");
        var nativeDialogs = Assert.Single(report, static item => item.Name == "nativeSystemDialogs");
        Assert.Equal(
            "capability-missing",
            webView.Outcome);
        Assert.Equal(
            "unsupported",
            nativeDialogs.Outcome);
    }

    [Fact]
    public void ExperimentalManifest_UsesAppKitLabelsWithoutMacCatalystEquivalence()
    {
        var root = Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "appkit-manifest-contract",
            Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = FlowPilotArtifactManifest.Create(new FlowPilotManifestOptions
            {
                ArtifactRoot = root,
                RepositoryRoot = AppFixtureBase.FindRepoRoot(),
                ManifestFileName = "appkit-tier1-manifest.json",
                Platform = "macos",
                Backend = "appkit",
                Experimental = true,
                OfficialCoverage = false,
                MacCatalystEquivalent = false,
                PackageId = PackageId,
                DeviceEvidenceKind = "desktop-host",
            });
            manifest.RecordInfrastructureFailure(
                MacOSTapFlow(),
                Path.Combine(
                    AppFixtureBase.FindRepoRoot(),
                    "samples",
                    "DevFlow.Sample.MacOS",
                    "maui-tests",
                    "appkit-native-baseline.md"),
                1,
                "static-contract");

            var write = manifest.WriteAtomic();

            Assert.True(write.Ok, write.Error);
            Assert.True(write.Validation?.IsValid, string.Join("; ", write.Validation?.Errors ?? []));
            using var document = JsonDocument.Parse(File.ReadAllText(write.Path!));
            var platform = document.RootElement.GetProperty("platform");
            Assert.True(platform.GetProperty("experimental").GetBoolean());
            Assert.Equal("appkit", platform.GetProperty("backend").GetString());
            Assert.False(platform.GetProperty("officialCoverage").GetBoolean());
            Assert.False(platform.GetProperty("macCatalystEquivalent").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TierOneCorpus_IsSmallAppKitOnlyAndUsesStableFixtureAutomationIds()
    {
        var flows = LoadTierOneFlows();
        Assert.InRange(flows.Count, 4, 8);
        foreach (var flow in flows)
        {
            Assert.Equal("macos", flow.Flow.Platform);
            Assert.Equal(PackageId, flow.Flow.App);
            Assert.True(FlowValidator.Validate(flow.Flow).Ok, flow.SourcePath);
        }

        var sample = ReadRepositoryFile("samples/DevFlow.Sample/MainPage.xaml");
        var interaction = ReadRepositoryFile("samples/DevFlow.Sample/InteractionTestPage.xaml");
        var modal = ReadRepositoryFile("samples/DevFlow.Sample/ModalTestPage.xaml");
        foreach (var automationId in new[]
                 {
                     "AddButton", "NewTodoEntry", "CountLabel", "ShowModalButton",
                 })
        {
            Assert.Contains(automationId, sample, StringComparison.Ordinal);
        }
        Assert.Contains("TestButton", interaction, StringComparison.Ordinal);
        Assert.Contains("CloseModalButton", modal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_UsesCanonicalRunnerAndFailsClosedForWrongPlatformOrMissingCapability()
    {
        var lifecycle = new FakeLifecycle();
        var driver = new FakeFlowDriver();
        await using var host = new MacOSFlowTestHost(lifecycle, driver);
        var result = await host.RunAsync(MacOSTapFlow(), CreateHostContractPlan());

        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome?.Status);
        Assert.Equal(
            ["build", "install", "reset", "launch", "wait", "seed", "verify"],
            lifecycle.Calls.Take(7).ToArray());
        Assert.Equal(1, driver.MutationCalls);

        var incompatible = MacOSTapFlow();
        incompatible.Platform = "maccatalyst";
        var rejected = await host.RunAsync(incompatible, CreateHostContractPlan());
        Assert.Equal(MauiFlowFailureClasses.CapabilityMissing, rejected.Report.Failure?.Code);
        Assert.Equal(1, driver.MutationCalls);

        var missingCapabilityLifecycle = new FakeLifecycle();
        var missingCapabilityDriver = new FakeFlowDriver();
        await using var missingCapabilityHost = new MacOSFlowTestHost(
            missingCapabilityLifecycle,
            missingCapabilityDriver);
        var missingCapability = await missingCapabilityHost.RunAsync(
            MacOSTapFlow(),
            CreateRequiredWebViewPlan());
        Assert.Equal(MauiFlowFailureClasses.CapabilityMissing, missingCapability.Report.Failure?.Code);
        Assert.Equal(0, missingCapabilityDriver.MutationCalls);
        Assert.Contains("wait", missingCapabilityLifecycle.Calls);
        Assert.DoesNotContain("seed", missingCapabilityLifecycle.Calls);
    }

    [Fact]
    public async Task SelectorRepairTrustAndReportContracts_AbstainWithoutAppKitParityClaims()
    {
        var report = await new MauiFlowRunner(new DuplicateAppKitIdentityDriver(), new MauiFlowRunnerOptions
        {
            RunId = "appkit-duplicate-selector",
            PollTries = 1,
            PollGapMs = 0,
        }).RunAsync(new MauiFlow
        {
            Name = "appkit-duplicate-selector",
            Platform = "macos",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "AddButton" } },
                },
            ],
        });
        Assert.Equal(MauiFlowFailureClasses.LocatorAmbiguous, report.Failure?.Code);

        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            IsCurrentLocalRun = true,
            ExpectedCheckpoint = AppKitCheckpoint(),
            CurrentCheckpoint = AppKitCheckpoint(),
            BeforeDispatch = false,
            Run = new MauiFlowRunReport
            {
                RunId = "appkit-unknown-completion",
                Failure = new MauiFlowFailure
                {
                    Code = MauiFlowFailureClasses.UnknownCompletion,
                    Class = MauiFlowFailureClasses.UnknownCompletion,
                },
            },
        });
        Assert.False(decision.Eligible);
        Assert.Contains(decision.Reasons, reason => reason.Blocking);

        var imported = new MauiArtifactTrustRecord
        {
            Identity = MauiImportedArtifactIdentity.Create(),
            Verification = new MauiArtifactTrustVerificationResult { State = MauiArtifactTrustStates.Untrusted },
        };
        Assert.False(MauiFlowRepairProposalTrustPolicy.CanCreateProposal(imported).Allowed);
        Assert.False(MauiSourceProposalTrustPolicy.CanCreateProposal(imported).Allowed);
    }

    /// <summary>
    /// Deferred, and deliberately visible as deferred. Source-proposal rollback is asserted here
    /// only through the trust policy; the C# and XAML source-proposal test suites that would prove
    /// a rollback on an AppKit target framework do not exist yet. This states what is owed instead
    /// of deleting the expectation. It is outside the Goal 4 MVP and must not be claimed as
    /// covered until this test runs green.
    /// </summary>
    [Fact(Skip = "Deferred beyond the Goal 4 MVP: no C#/XAML source-proposal rollback suites exist for the AppKit target. Not covered; do not claim it.")]
    public void SourceProposalSuites_CoverRollbackOnTheAppKitTargetFramework()
    {
        var csharp = ReadRepositoryFile("src/DevFlow/Microsoft.Maui.DevFlow.Tests/CSharpSourceProposalTests.cs");
        var xaml = ReadRepositoryFile("src/DevFlow/Microsoft.Maui.DevFlow.Tests/XamlSourceProposalTests.cs");
        Assert.Contains("Rollback", csharp, StringComparison.Ordinal);
        Assert.Contains("Rollback", xaml, StringComparison.Ordinal);
        Assert.Contains("net10.0-macos", csharp, StringComparison.Ordinal);
        Assert.Contains("net10.0-macos", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptWorkflowAndDocumentation_KeepAppKitExperimentalAndSeparate()
    {
        var shell = ReadRepositoryFile("eng/devflow/Run-DevFlowFlowQa.sh");
        var powershell = ReadRepositoryFile("eng/devflow/Run-DevFlowFlowQa.ps1");
        var workflow = ReadRepositoryFile(".github/workflows/devflow-integration.yml");
        var docs = ReadRepositoryFile("docs/DevFlow/flow-qa.md");

        Assert.Contains("macos-appkit-flow-qa:", workflow, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", workflow, StringComparison.Ordinal);
        Assert.Contains("--platform macos", workflow, StringComparison.Ordinal);
        Assert.Contains("--experimental", workflow, StringComparison.Ordinal);
        Assert.Contains("Evaluate experimental AppKit evidence mapping", workflow, StringComparison.Ordinal);
        Assert.Contains("devflow flow qualify --platform macos", workflow, StringComparison.Ordinal);
        Assert.Contains("backend", shell, StringComparison.Ordinal);
        Assert.Contains("appkit", shell, StringComparison.Ordinal);
        Assert.Contains("macCatalystEquivalent", shell, StringComparison.Ordinal);
        Assert.Contains("backend", powershell, StringComparison.Ordinal);
        Assert.Contains("appkit", powershell, StringComparison.Ordinal);
        Assert.Contains("macCatalystEquivalent", powershell, StringComparison.Ordinal);
        Assert.Contains("DevFlow.Sample.MacOS", shell, StringComparison.Ordinal);
        Assert.Contains("DevFlow.Sample.MacOS", powershell, StringComparison.Ordinal);
        Assert.Contains("not Mac Catalyst coverage", docs, StringComparison.Ordinal);
    }

    [Fact]
    public void ExperimentalAppKitWorkflow_ParsesAsYaml()
    {
        var source = ReadRepositoryFile(".github/workflows/devflow-integration.yml");
        var workflow = new DeserializerBuilder().Build().Deserialize(new StringReader(source));

        Assert.NotNull(workflow);
    }

    [AppKitFlowQaFact]
    public async Task Runtime_AppKitFixtureExecutesTierOneAndWritesExperimentalArtifacts_WhenEnabled()
    {
        var repositoryRoot = AppFixtureBase.FindRepoRoot();
        var artifactRoot = ResolveArtifactRoot(repositoryRoot);
        var manifest = FlowPilotArtifactManifest.Create(new FlowPilotManifestOptions
        {
            ArtifactRoot = artifactRoot,
            RepositoryRoot = repositoryRoot,
            ManifestFileName = "appkit-tier1-manifest.json",
            Platform = MacOSFlowCapabilities.PlatformKey,
            Backend = MacOSFlowCapabilities.Backend,
            Experimental = true,
            OfficialCoverage = false,
            MacCatalystEquivalent = false,
            PackageId = PackageId,
            DeviceEvidenceKind = "desktop-host",
        });
        var failures = new List<string>();
        MacOSFixture? fixture = null;
        MacOSFlowTestHost? host = null;

        try
        {
            fixture = new MacOSFixture();
            await fixture.InitializeAsync();
            host = fixture.CreateFlowTestHost();
            await VerifyRuntimeContractAsync(fixture, artifactRoot, failures);

            var repetitions = ResolveCleanRepetitions(Environment.GetEnvironmentVariable("DEVFLOW_FLOW_QA_REPEAT"));
            foreach (var flow in LoadTierOneFlows())
            {
                for (var repetition = 1; repetition <= repetitions; repetition++)
                {
                    var runId = $"{flow.Flow.Name}-attempt-{repetition}";
                    var runDirectory = Path.Combine(artifactRoot, runId);
                    Directory.CreateDirectory(runDirectory);
                    var result = await host.RunAsync(
                        flow.Flow,
                        CreatePlan(flow.Flow),
                        new AppleFlowRunRequest
                        {
                            RunId = runId,
                            ArtifactRoot = artifactRoot,
                            FailureEvidenceCapture = new FlowReplayEvidenceCapture(
                                fixture.Client,
                                Path.Combine(runDirectory, "failure.mauitrace"),
                                repositoryRoot,
                                repositoryRoot,
                                "appkit-flow-qa"),
                        });
                    manifest.RecordCleanAttempt(flow.Flow, flow.SourcePath, repetition, result.Report);
                    if (!string.Equals(result.Report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{flow.Flow.Name} attempt {repetition}: {result.Report.Outcome?.Status ?? "unknown"} " +
                            $"({result.Report.Failure?.Code ?? "no-failure-code"}).");
                    }
                }
            }
        }
        catch (PlatformFlowLifecycleException exception)
        {
            foreach (var flow in LoadTierOneFlows())
            {
                if (!manifest.Flows.Any(entry => string.Equals(entry.Name, flow.Flow.Name, StringComparison.Ordinal)))
                {
                    manifest.RecordInfrastructureFailure(
                        flow.Flow,
                        flow.SourcePath,
                        repetition: 1,
                        phase: "appkit-fixture-initialization");
                }
            }
            failures.Add($"{exception.Kind.ToString().ToLowerInvariant()}: {exception.Message}");
        }
        finally
        {
            if (host is not null)
            {
                try { await host.DisposeAsync(); }
                catch (Exception exception) { manifest.AddOmission("host-cleanup", exception.GetType().Name); }
            }
            if (fixture is not null)
            {
                try { await fixture.DisposeAsync(); }
                catch (Exception exception) { manifest.AddOmission("fixture-cleanup", exception.GetType().Name); }
            }

            var write = manifest.WriteAtomic();
            if (!write.Ok)
                failures.Add(write.Error ?? "The AppKit Tier-1 manifest could not be written.");
            else if (write.Validation is { IsValid: false })
                failures.AddRange(write.Validation.Errors);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [AppKitFlowQaFact]
    public void RuntimeManifest_RequiresExperimentalAppKitLabels_WhenEnabled()
    {
        var path = Environment.GetEnvironmentVariable("DEVFLOW_APPKIT_QA_MANIFEST")
            ?? throw new InvalidOperationException(
                "DEVFLOW_APPKIT_QA_MANIFEST is required when experimental AppKit QA is enabled.");
        Assert.True(File.Exists(path), $"Experimental AppKit manifest was not produced: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("macos", root.GetProperty("platform").GetString());
        Assert.True(root.GetProperty("experimental").GetBoolean());
        Assert.Equal("appkit", root.GetProperty("backend").GetString());
        Assert.False(root.GetProperty("macCatalystEquivalent").GetBoolean());
        Assert.False(root.GetProperty("officialCoverage").GetBoolean());
    }

    static async Task VerifyRuntimeContractAsync(
        MacOSFixture fixture,
        string artifactRoot,
        List<string> failures)
    {
        var outcomes = new List<object>();
        var capabilityGaps = new List<string>();
        try
        {
            var add = Assert.Single(await fixture.Client.QueryAsync(automationId: "AddButton"));
            if (!string.Equals(add.NativeAutomationIdentity, "AddButton", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(add.NativeAutomationIdentityKind))
            {
                capabilityGaps.Add("appkit-native-accessibility-identity");
                outcomes.Add(new { name = "managedNativeIdentity", status = "capability-missing" });
            }
            else
            {
                outcomes.Add(new { name = "managedNativeIdentity", status = "passed" });
            }

            await fixture.Client.NavigateAsync("//interactions");
            await WaitForRouteAsync(fixture.Client, "interactions");
            var navigationStatus = await fixture.Client.GetStatusAsync();
            if (string.IsNullOrWhiteSpace(navigationStatus?.Window))
            {
                capabilityGaps.Add("agent.window-checkpoint");
                outcomes.Add(new { name = "shellWindow", status = "capability-missing" });
            }
            else
            {
                outcomes.Add(new { name = "shellWindow", status = "passed" });
            }

            await fixture.Client.NavigateAsync("//native");
            await WaitForElementAsync(fixture.Client, "AddButton");
            var showModal = Assert.Single(await fixture.Client.QueryAsync(automationId: "ShowModalButton"));
            Assert.True(await fixture.Client.TapAsync(showModal.Id));
            await WaitForElementAsync(fixture.Client, "ModalTitle");
            var modalStatus = await fixture.Client.GetStatusAsync();
            if (string.IsNullOrWhiteSpace(modalStatus?.Modal))
            {
                capabilityGaps.Add("agent.modal-checkpoint");
                outcomes.Add(new { name = "modalEquivalent", status = "capability-missing" });
            }
            else
            {
                outcomes.Add(new { name = "modalEquivalent", status = "passed" });
            }
            var closeModal = Assert.Single(await fixture.Client.QueryAsync(automationId: "CloseModalButton"));
            Assert.True(await fixture.Client.TapAsync(closeModal.Id));
            await WaitForElementAsync(fixture.Client, "AddButton");

            var entry = Assert.Single(await fixture.Client.QueryAsync(automationId: "NewTodoEntry"));
            if (string.IsNullOrWhiteSpace(entry.NativeAutomationIdentityKind))
            {
                capabilityGaps.Add("appkit-native-entry-identity");
                outcomes.Add(new { name = "nativeControls", status = "capability-missing" });
            }
            else
            {
                outcomes.Add(new { name = "nativeControls", status = "passed" });
            }

            var status = await fixture.Client.GetStatusAsync();
            if (status?.Capabilities?.WebView == true)
            {
                await fixture.Client.NavigateAsync("//blazor");
                var contexts = await fixture.Client.GetCdpWebViewsAsync();
                var available = contexts.ValueKind == JsonValueKind.Array
                    ? contexts.GetArrayLength() > 0
                    : contexts.TryGetProperty("webviews", out var webviews) &&
                      webviews.ValueKind == JsonValueKind.Array &&
                      webviews.GetArrayLength() > 0;
                if (available)
                    outcomes.Add(new { name = "webViewContext", status = "passed" });
                else
                    capabilityGaps.Add("agent.webview-context");
            }
            else
            {
                outcomes.Add(new { name = "webViewContext", status = "unsupported" });
            }
        }
        catch (Exception exception) when (exception is Xunit.Sdk.XunitException or HttpRequestException or IOException or TimeoutException)
        {
            failures.Add($"infrastructure-failure: {exception.Message}");
            outcomes.Add(new { name = "runtimeLifecycle", status = "infrastructure-failure" });
        }
        finally
        {
            await WriteRuntimeContractArtifactAsync(artifactRoot, outcomes, capabilityGaps).ConfigureAwait(false);
        }

        if (capabilityGaps.Count > 0)
            failures.Add($"capability-missing: {string.Join(", ", capabilityGaps)}");
    }

    static async Task WaitForRouteAsync(AgentClient client, string routePart)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await client.GetStatusAsync())?.Route?.Contains(routePart, StringComparison.OrdinalIgnoreCase) == true)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"The AppKit agent did not report route containing '{routePart}'.");
    }

    static async Task WaitForElementAsync(AgentClient client, string automationId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await client.QueryAsync(automationId: automationId)).Count == 1)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"The AppKit agent did not expose '{automationId}'.");
    }

    static async Task WriteRuntimeContractArtifactAsync(
        string artifactRoot,
        IReadOnlyList<object> outcomes,
        IReadOnlyList<string> capabilityGaps)
    {
        Directory.CreateDirectory(artifactRoot);
        var path = Path.Combine(artifactRoot, "appkit-capabilities.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new
                {
                    schema = 1,
                    kind = "devflow-appkit-capability-contract",
                    platform = "macos",
                    backend = "appkit",
                    experimental = true,
                    officialCoverage = false,
                    macCatalystEquivalent = false,
                    generatedAt = DateTimeOffset.UtcNow,
                    matrix = MacOSFlowCapabilities.Matrix,
                    outcomes,
                    capabilityGaps,
                }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    static int ResolveCleanRepetitions(string? configured)
        => int.TryParse(configured, out var value) && value is >= 1 and <= 20
            ? value
            : DefaultCleanRepetitions;

    static string ResolveArtifactRoot(string repositoryRoot)
    {
        var configured = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_QA_ARTIFACT_ROOT");
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(repositoryRoot, "artifacts", "TestResults", "devflow-flow", "macos")
            : Path.GetFullPath(configured);
        var relative = Path.GetRelativePath(repositoryRoot, root);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Experimental AppKit artifacts must remain under the repository.");
        return root;
    }

    static List<FlowPilotFlowSource> LoadTierOneFlows()
    {
        var directory = Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "samples",
            "DevFlow.Sample.MacOS",
            "maui-tests");
        return Directory.GetFiles(directory, "*.md")
            .Where(static path => !string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var parsed = FlowMarkdown.Parse(File.ReadAllText(path), path);
                if (!parsed.Ok || parsed.Flow is null)
                    throw new InvalidOperationException($"{Path.GetFileName(path)} could not be parsed: {parsed.Error}");
                return new FlowPilotFlowSource(path, parsed.Flow);
            })
            .ToList();
    }

    static MauiFlow MacOSTapFlow() => new()
    {
        Name = "appkit-host-tap",
        Platform = "macos",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
            },
        ],
    };

    static MauiTestPlan CreatePlan(MauiFlow flow)
        => CreateValidPlan(
            flow,
            $"appkit-flow-qa-{flow.Name ?? "contract"}",
            "Exercise the AppKit flow QA contract.",
            new MauiFlowRequirements
            {
                RequiredCapabilities =
                [
                    new MauiCapabilityRequirement { Name = "agent.ui", Required = true },
                    new MauiCapabilityRequirement { Name = "agent.mutations", Required = true },
                    new MauiCapabilityRequirement { Name = "agent.workflowCommandLedger", Required = true },
                ],
            },
            [
                new MauiIndependentBusinessOracleDeclaration
                {
                    OracleId = "sample-integration-state",
                    Required = true,
                    Independent = true,
                },
            ]);

    static MauiTestPlan CreateHostContractPlan()
        => CreateValidPlan(
            MacOSTapFlow(),
            "appkit-host-contract",
            "Exercise the AppKit host contract.");

    static MauiTestPlan CreateRequiredWebViewPlan()
        => CreateValidPlan(
            MacOSTapFlow(),
            "appkit-required-webview-contract",
            "Verify that a required AppKit WebView capability fails closed.",
            new MauiFlowRequirements
            {
                RequiredCapabilities =
                [
                    new MauiCapabilityRequirement { Name = "agent.webview", Required = true },
                ],
            });

    static MauiTestPlan CreateValidPlan(
        MauiFlow flow,
        string planId,
        string goal,
        MauiFlowRequirements? requirements = null,
        List<MauiIndependentBusinessOracleDeclaration>? independentBusinessOracles = null)
        => new()
        {
            PlanId = planId,
            Revision = 1,
            Goal = goal,
            Flow = new MauiFlowReference
            {
                Path = $"{flow.Name ?? planId}.md",
                FlowId = flow.Name ?? planId,
                Revision = 1,
                Digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
            },
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            RequiredPlatforms = ["macos"],
            Requirements = requirements,
            Reset = new MauiTestResetRequirement
            {
                Required = false,
                Strategy = "host-owned",
            },
            Checkpoint = new MauiFlowCheckpointRequirements { Route = "//native" },
            IndependentBusinessOracles = independentBusinessOracles ?? [],
            Provenance = new MauiActorProvenance
            {
                ActorKind = "test",
                ActorId = "appkit-flow-qa",
                Channel = "integration-test",
                Provider = "xunit",
            },
        };

    static MauiFlowCheckpoint AppKitCheckpoint() => new()
    {
        AppBuildFingerprint = "sha256:appkit-build",
        AgentInstanceId = "appkit-agent",
        SeedFingerprint = "sha256:seed",
        BackendStateFingerprint = "sha256:backend",
        Route = "//native",
        Window = "main",
        Locale = "en-US",
        Theme = "light",
        DisplayProfile = "appkit-desktop",
    };

    sealed class DuplicateAppKitIdentityDriver : IMauiFlowDriver
    {
        readonly List<ElementInfo> _elements =
        [
            Element("one"),
            Element("two"),
        ];

        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(
                string.Equals(automationId, "AddButton", StringComparison.Ordinal)
                    ? _elements
                    : []);

        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult<ElementInfo?>(_elements.FirstOrDefault(element => element.Id == id));

        public Task<bool> TapAsync(string elementId) => Task.FromResult(true);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(false);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(false);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(false);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(false);
        public Task<bool> BackAsync() => Task.FromResult(false);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Theme = theme, Success = false });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus { Running = true });
        public Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0) => Task.FromResult(_elements);

        static ElementInfo Element(string id) => new()
        {
            Id = id,
            AutomationId = "AddButton",
            NativeAutomationIdentity = "AddButton",
            NativeAutomationIdentityKind = "appkit-accessibility-identifier",
            Type = "Button",
            IsVisible = true,
            IsEnabled = true,
            Opacity = 1,
            Bounds = new BoundsInfo { Width = 20, Height = 20 },
        };
    }

    static string ReadRepositoryFile(string relativePath)
        => File.ReadAllText(Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
