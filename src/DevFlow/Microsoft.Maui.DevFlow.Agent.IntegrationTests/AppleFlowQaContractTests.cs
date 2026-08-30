using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Apple QA contract checks. Runtime assertions are deliberately gated so Windows compilation
/// proves the host contract without claiming a simulator or Mac Catalyst execution occurred.
/// </summary>
[Trait("Category", "AppleTestAgent")]
public sealed class AppleFlowQaContractTests
{
    [Fact]
    public void RuntimeGate_RequiresMacOSAndExplicitHostOptIn()
    {
        var enabled = AppleQaEnvironment.IsRuntimeEnabled;
        if (!OperatingSystem.IsMacOS())
            Assert.False(enabled);
    }

    [Fact]
    public async Task AppleLifecycleHost_ResetsBeforeCanonicalRunnerAndPersistsReport()
    {
        var root = Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "apple-flow-host-contract",
            Guid.NewGuid().ToString("N"));
        try
        {
            var lifecycle = new FakeLifecycle
            {
                Reset = new MauiFlowResetResult
                {
                    Requested = true,
                    Succeeded = true,
                    AppStateSucceeded = true,
                    BackendTestDataSucceeded = true,
                    Strategy = "uninstall-reinstall",
                    SeedFingerprint = "seed",
                    BackendStateFingerprint = "backend",
                },
            };
            await using var host = new AppleFlowTestHost("ios", lifecycle, new FakeFlowDriver());
            var result = await host.RunAsync(
                TapFlow(),
                Plan(),
                new AppleFlowRunRequest { ArtifactRoot = root, RunId = "ios-contract" });

            Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome?.Status);
            Assert.NotNull(result.Report.ReportPath);
            Assert.True(File.Exists(result.Report.ReportPath));
            Assert.Equal(
                ["build", "install", "reset", "install", "launch", "wait", "seed", "verify"],
                lifecycle.Calls.Take(8).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateNativeIdentity_BlocksMutationWithoutFallback()
    {
        var driver = new DuplicateIdentityDriver();
        var report = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            RunId = "apple-duplicate-selector",
            PollTries = 1,
            PollGapMs = 0,
        }).RunAsync(TapFlow());

        Assert.Equal(MauiFlowRunOutcomes.Failed, report.Outcome?.Status);
        Assert.Equal(0, driver.MutationCalls);
        Assert.NotEmpty(report.Steps);
    }

    /// <summary>
    /// Deferred, and deliberately visible as deferred. The iOS Simulator and Mac Catalyst fixtures
    /// do not yet declare a platform-specific hard reset and seed contract - no
    /// <c>simctl uninstall</c> reinstall cycle, no seeded state, no checkpoint verification - so
    /// this states the contract those fixtures owe rather than deleting the expectation. It is
    /// outside the Goal 4 MVP and must not be claimed as covered until this test runs green.
    /// </summary>
    [Fact(Skip = "Deferred beyond the Goal 4 MVP: the Apple fixtures do not yet implement a hard reset and seed contract. Not covered; do not claim it.")]
    public void AppleFixtures_DeclarePlatformSpecificHardResetAndSeedContracts()
    {
        var root = AppFixtureBase.FindRepoRoot();
        var ios = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.IntegrationTests",
            "Fixtures",
            "iOSSimulatorFixture.cs"));
        var catalyst = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.IntegrationTests",
            "Fixtures",
            "MacCatalystFixture.cs"));

        Assert.Contains("simctl uninstall", ios, StringComparison.Ordinal);
        Assert.Contains("simctl install", ios, StringComparison.Ordinal);
        Assert.Contains("SeedAsync", ios, StringComparison.Ordinal);
        Assert.Contains("VerifyCheckpointAsync", ios, StringComparison.Ordinal);
        Assert.Contains("safe-test-relaunch", catalyst, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_INTEGRATION_TEST_SEED", catalyst, StringComparison.Ordinal);
        Assert.Contains("SeedAsync", catalyst, StringComparison.Ordinal);
        Assert.DoesNotContain("net10.0-macos", catalyst, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairEligibility_AbstainsForUnknownAppleOperationCompletion()
    {
        var checkpoint = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = "sha256:build",
            AgentInstanceId = "apple-agent",
            SeedFingerprint = "seed",
            BackendStateFingerprint = "backend",
            Route = "//native",
        };
        var decision = MauiFlowRepairEligibilityEvaluator.Evaluate(new MauiFlowRepairEligibilityInput
        {
            IsCurrentLocalRun = true,
            ExpectedCheckpoint = checkpoint,
            CurrentCheckpoint = checkpoint,
            BeforeDispatch = false,
            Run = new MauiFlowRunReport
            {
                RunId = "apple-unknown",
                Failure = new MauiFlowFailure
                {
                    Code = MauiFlowFailureClasses.UnknownCompletion,
                    Class = MauiFlowFailureClasses.UnknownCompletion,
                    Phase = "dispatch",
                },
            },
        });

        Assert.False(decision.Eligible);
        Assert.Contains(decision.Reasons, reason => reason.Blocking);
    }

    [Fact]
    public void ImportedAppleArtifact_CannotAuthorizeRepairOrSourceApply()
    {
        var artifact = new MauiArtifactTrustRecord
        {
            Identity = MauiImportedArtifactIdentity.Create(),
            Verification = new MauiArtifactTrustVerificationResult
            {
                State = MauiArtifactTrustStates.Untrusted,
            },
        };

        Assert.False(MauiFlowRepairProposalTrustPolicy.CanCreateProposal(artifact).Allowed);
        Assert.False(MauiSourceProposalTrustPolicy.CanCreateProposal(artifact).Allowed);
    }

    [AppleFlowQaFact]
    public void RuntimeArtifactManifest_CoversRequiredAppleContractOutcomes_WhenEnabled()
    {
        var path = AppleQaEnvironment.RequiredManifestPath();
        Assert.True(File.Exists(path), $"Apple QA manifest was not produced: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("devflow-apple-flow-qa", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("platform").GetString() is "ios" or "maccatalyst" or "macos");
        if (root.GetProperty("platform").GetString() == "macos")
        {
            Assert.True(root.GetProperty("experimental").GetBoolean());
            Assert.Equal("appkit", root.GetProperty("backend").GetString());
            Assert.False(root.GetProperty("officialCoverage").GetBoolean());
            Assert.False(root.GetProperty("macCatalystEquivalent").GetBoolean());
        }

        var spike = root.GetProperty("spike");
        Assert.Equal("proved", spike.GetProperty("status").GetString());
        Assert.True(spike.GetProperty("foregroundProof").GetBoolean());
        Assert.True(spike.GetProperty("authenticatedTransport").GetBoolean());
        Assert.True(spike.GetProperty("receipt").GetBoolean());
        Assert.True(spike.GetProperty("cancellation").GetBoolean());
        Assert.True(spike.GetProperty("parity").GetBoolean());
        var checkpoint = root.GetProperty("checkpoint");
        Assert.StartsWith("sha256:", checkpoint.GetProperty("seedFingerprint").GetString());
        Assert.StartsWith("sha256:", checkpoint.GetProperty("backendStateFingerprint").GetString());

        var flows = root.GetProperty("flows").EnumerateArray().ToArray();
        Assert.InRange(flows.Length, 7, 12);
        foreach (var flow in flows)
        {
            Assert.Equal(3, flow.GetProperty("cleanAttempts").GetArrayLength());
            Assert.Equal("passed", flow.GetProperty("firstAttempt").GetProperty("status").GetString());
        }

        var outcomes = root.GetProperty("contractOutcomes");
        foreach (var name in new[]
        {
            "agentAuthoredFlow",
            "selectorIdentity",
            "shellModalRoute",
            "webViewContext",
            "repairAbstention",
            "sourceProposal",
            "securityPrivacy",
            "reportParity",
        })
        {
            Assert.True(outcomes.TryGetProperty(name, out var value), $"Missing Apple QA outcome '{name}'.");
            Assert.NotEqual("not-run", value.GetString());
        }
    }

    static MauiFlow TapFlow() => new()
    {
        Name = "apple-contract-tap",
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

    static MauiTestPlan Plan()
    {
        var flow = TapFlow();
        return new()
        {
            PlanId = "apple-contract-plan",
            Revision = 1,
            Goal = "Exercise the Apple flow host contract.",
            Flow = new MauiFlowReference
            {
                Path = "apple-contract-tap.md",
                FlowId = "apple-contract-tap",
                Revision = 1,
                Digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
            },
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            Reset = new MauiTestResetRequirement
            {
                Required = false,
                Strategy = "host-owned",
            },
            Checkpoint = new MauiFlowCheckpointRequirements { Route = "//native" },
            Provenance = new MauiActorProvenance
            {
                ActorKind = "test",
                ActorId = "apple-flow-qa",
                Channel = "integration-test",
                Provider = "xunit",
            },
        };
    }

    sealed class DuplicateIdentityDriver : IMauiFlowDriver
    {
        readonly List<ElementInfo> _elements =
        [
            Element("one"),
            Element("two"),
        ];

        public int MutationCalls { get; private set; }
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(
                string.Equals(automationId, "submit", StringComparison.Ordinal)
                    ? _elements
                    : []);
        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult(_elements.FirstOrDefault(element => element.Id == id));
        public Task<bool> TapAsync(string elementId) { MutationCalls++; return Task.FromResult(true); }
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
            AutomationId = "submit",
            Type = "Button",
            IsVisible = true,
            IsEnabled = true,
            Opacity = 1,
            Bounds = new BoundsInfo { Width = 20, Height = 20 },
            NativeAutomationIdentity = "submit",
            NativeAutomationIdentityKind = "accessibility-identifier",
        };
    }
}

internal static class AppleQaEnvironment
{
    internal static bool IsRuntimeEnabled => FlowQaLaneGate.AppleFlowQa().IsEnabled;

    internal static string RequiredManifestPath()
        => Environment.GetEnvironmentVariable("DEVFLOW_APPLE_QA_MANIFEST")
            ?? throw new InvalidOperationException(
                "DEVFLOW_APPLE_QA_MANIFEST is required when DEVFLOW_RUN_APPLE_FLOW_QA=1.");
}
