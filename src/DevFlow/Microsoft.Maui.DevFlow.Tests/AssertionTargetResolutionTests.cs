using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Regression coverage for assertion-level selector drift. Breaking an <em>action's</em> selector
/// was already reported as <c>test-drift</c>, but breaking an <em>assertion's</em> selector was
/// reported as <c>assertionFailed</c> and projected to <c>app-regression</c> — telling the author
/// their app had changed when only the test had. The reports carried no resolution outcome for
/// assertion targets at all, so nothing downstream could tell the two apart.
/// </summary>
public class AssertionTargetResolutionTests
{
    private const string PresentId = "TodoCountLabel";
    private const string MissingId = "RenamedCountLabel";

    [Fact]
    public async Task RunAsync_AssertionSelectorNoLongerResolves_IsTestDriftNotAppRegression()
    {
        var driver = new SingleElementDriver { PropertyValue = "1 item" };

        var result = await RunAsync(driver, MissingId, expected: "1 item");

        var step = Assert.Single(result.Report.Steps);
        var assertion = Assert.Single(step.Assertions);
        Assert.False(assertion.Passed);
        Assert.Equal(FlowFailureKinds.NotFound, assertion.TargetResolution?.Status);
        Assert.Equal(0, assertion.TargetResolution?.MatchCount);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, step.FailureClass);
        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, result.Report.Failure?.Class);
        Assert.Equal(
            MauiFlowTriageDispositions.TestDrift,
            MauiFlowFailureClassifier.Project(result.Report.Failure?.Class));
    }

    [Fact]
    public async Task RunAsync_AssertionValueDiffersOnAResolvedSelector_StaysAnAppRegression()
    {
        // The selector still names exactly one element, so the app really did render something
        // else. This must keep pointing at the app, or the fix above would hide real regressions.
        var driver = new SingleElementDriver { PropertyValue = "0 items" };

        var result = await RunAsync(driver, PresentId, expected: "1 item");

        var step = Assert.Single(result.Report.Steps);
        var assertion = Assert.Single(step.Assertions);
        Assert.False(assertion.Passed);
        Assert.Equal("resolved", assertion.TargetResolution?.Status);
        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, step.FailureClass);
        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, result.Report.Failure?.Class);
        Assert.Equal(
            MauiFlowTriageDispositions.AppRegression,
            MauiFlowFailureClassifier.Project(result.Report.Failure?.Class));
    }

    [Fact]
    public async Task RunAsync_PassingAssertion_RecordsTheResolvedTarget()
    {
        var driver = new SingleElementDriver { PropertyValue = "1 item" };

        var result = await RunAsync(driver, PresentId, expected: "1 item");

        var assertion = Assert.Single(Assert.Single(result.Report.Steps).Assertions);
        Assert.True(assertion.Passed);
        Assert.Equal("resolved", assertion.TargetResolution?.Status);
        Assert.Equal(1, assertion.TargetResolution?.MatchCount);
    }

    [Fact]
    public async Task RunAsync_NotExistsAssertionThatFinds_TheElement_StaysAnAppRegression()
    {
        // For notExists a selector that does not resolve *is* the expectation, so a failure means
        // the element was present. Reading that as selector drift would blame the test for a real
        // app change, which is the same mistake in the opposite direction.
        var driver = new SingleElementDriver { PropertyValue = "1 item" };
        var flow = new MauiFlow
        {
            Name = "assert-absent",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "notExists",
                            Verify = true,
                            Selector = new FlowSelector { AutomationId = PresentId },
                        },
                    ],
                },
            },
        };

        var result = await new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunAsync(flow);

        var step = Assert.Single(result.Steps);
        var assertion = Assert.Single(step.Assertions);
        Assert.False(assertion.Passed);
        Assert.Null(assertion.TargetResolution);
        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, step.FailureClass);
        Assert.Equal(
            MauiFlowTriageDispositions.AppRegression,
            MauiFlowFailureClassifier.Project(result.Failure?.Class));
    }

    [Theory]
    [InlineData(FlowFailureKinds.NotFound, MauiFlowFailureClasses.LocatorNotFound, MauiFlowTriageDispositions.TestDrift)]
    [InlineData(FlowFailureKinds.Ambiguous, MauiFlowFailureClasses.LocatorAmbiguous, MauiFlowTriageDispositions.TestDrift)]
    [InlineData("resolved", MauiFlowFailureClasses.AssertionFailed, MauiFlowTriageDispositions.AppRegression)]
    [InlineData(null, MauiFlowFailureClasses.AssertionFailed, MauiFlowTriageDispositions.AppRegression)]
    public void Classify_AssertionFailure_FollowsTheAssertionTargetResolution(
        string? resolution,
        string expectedClass,
        string expectedDisposition)
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.Assertion,
            AssertionTargetResolution = resolution,
        });

        Assert.Equal(expectedClass, classification.FailureClass);
        Assert.Equal(expectedDisposition, MauiFlowFailureClassifier.Project(classification.FailureClass));
    }

    [Fact]
    public void Classify_StampedAssertionFailedWithAnUnresolvedTarget_IsCorrectedToLocatorDrift()
    {
        // Reports written before this signal existed stamp `assertionFailed` from the legacy kind
        // alone, so the correction has to win over the stamp or re-reading a report keeps the
        // wrong answer.
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.AssertionFailed,
            LegacyFailureKind = FlowFailureKinds.Assertion,
            AssertionTargetResolution = FlowFailureKinds.NotFound,
        });

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, classification.FailureClass);
        Assert.Equal(MauiFlowClassificationBases.Inferred, classification.Basis);
    }

    [Fact]
    public void Classify_NonAssertionFailure_IgnoresTheAssertionTargetResolution()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.Disabled,
            AssertionTargetResolution = FlowFailureKinds.NotFound,
        });

        Assert.Equal(MauiFlowFailureClasses.Disabled, classification.FailureClass);
    }

    [Fact]
    public void Classify_UnresolvedAssertionTargetOffRoute_PrefersStateDriftOverLocatorDrift()
    {
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.Assertion,
            AssertionTargetResolution = FlowFailureKinds.NotFound,
            RouteMatches = false,
        });

        Assert.Equal(MauiFlowFailureClasses.RouteStateDrift, classification.FailureClass);
        Assert.Equal(
            MauiFlowTriageDispositions.TestDrift,
            MauiFlowFailureClassifier.Project(classification.FailureClass));
    }

    [Fact]
    public async Task Analyze_ReportWithAnUnresolvedAssertionTarget_ReportsTestDrift()
    {
        var driver = new SingleElementDriver { PropertyValue = "1 item" };
        var result = await RunAsync(driver, MissingId, expected: "1 item");

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = result.Report });

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, triage.Classification?.FailureClass);
        Assert.Equal(MauiFlowTriageDispositions.TestDrift, triage.Classification?.Disposition);
    }

    [Fact]
    public async Task Analyze_ReportWithAResolvedAssertionTarget_StillReportsAppRegression()
    {
        var driver = new SingleElementDriver { PropertyValue = "0 items" };
        var result = await RunAsync(driver, PresentId, expected: "1 item");

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = result.Report });

        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, triage.Classification?.FailureClass);
        Assert.Equal(MauiFlowTriageDispositions.AppRegression, triage.Classification?.Disposition);
    }

    [Fact]
    public void Classify_UnresolvedAssertionTarget_NeverUnlocksSelectorRepair()
    {
        // Selector repair rewrites a step's action selector. On an assertion-drift step that
        // selector is the one still resolving correctly, so offering repair would replace the
        // wrong selector and leave the broken one in place.
        var facts = new MauiFlowFailureFacts
        {
            LegacyFailureKind = FlowFailureKinds.Assertion,
            AssertionTargetResolution = FlowFailureKinds.NotFound,
            BeforeDispatch = true,
            CheckpointVerified = true,
            CheckpointMatches = true,
            RouteMatches = true,
        };

        var classification = MauiFlowFailureClassifier.Classify(facts);

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, classification.FailureClass);
        Assert.False(classification.RepairEligible);

        // The same preconditions on genuine action-selector drift still are repair eligible, so
        // the guard is scoped to the assertion signal rather than disabling repair outright.
        facts.LegacyFailureKind = FlowFailureKinds.NotFound;
        facts.AssertionTargetResolution = null;
        Assert.True(MauiFlowFailureClassifier.Classify(facts).RepairEligible);
    }

    [Fact]
    public void Classify_AlreadyCorrectedAssertionDrift_StaysRepairIneligible()
    {
        // Triage re-reads a stored report, where the class is already `locatorNotFound`. The
        // correction branch no longer fires, so the guard has to key off the assertion fact.
        var classification = MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
        {
            FailureClass = MauiFlowFailureClasses.LocatorNotFound,
            LegacyFailureKind = FlowFailureKinds.Assertion,
            AssertionTargetResolution = FlowFailureKinds.NotFound,
            BeforeDispatch = true,
            CheckpointVerified = true,
            CheckpointMatches = true,
            RouteMatches = true,
        });

        Assert.Equal(MauiFlowFailureClasses.LocatorNotFound, classification.FailureClass);
        Assert.False(classification.RepairEligible);
    }

    [Fact]
    public async Task Analyze_ReportWithAnUnresolvedAssertionTarget_DoesNotRecommendSelectorRepair()
    {
        var driver = new SingleElementDriver { PropertyValue = "1 item" };
        var result = await RunAsync(driver, MissingId, expected: "1 item");

        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput { Report = result.Report });

        Assert.False(triage.RepairEligible);
        Assert.DoesNotContain(MauiFlowTriageNextActions.ReviewSelectorRepair, triage.AllowedNextActions);
    }

    [Fact]
    public async Task ToLegacy_CarriesTheAssertionTargetResolutionOntoTheLegacyShape()
    {
        // `flow replay` and the inspector emit the legacy shape, so the signal has to survive the
        // adapter or those surfaces still cannot tell selector drift from an app regression.
        var driver = new SingleElementDriver { PropertyValue = "1 item" };
        var result = await RunAsync(driver, MissingId, expected: "1 item");

        var legacy = FlowReplayReportAdapter.ToLegacy(result.Report);

        var assertion = Assert.Single(Assert.Single(legacy.Results).Asserts);
        Assert.Equal(FlowFailureKinds.NotFound, assertion.TargetStatus);
        Assert.Equal(0, assertion.TargetMatchCount);
    }

    private static Task<MauiFlowRunExecutionResult> RunAsync(
        SingleElementDriver driver,
        string assertionAutomationId,
        string expected)
    {
        var flow = new MauiFlow
        {
            Name = "assert-count",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = PresentId },
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "propEquals",
                            Verify = true,
                            Name = "Text",
                            Expected = expected,
                            Selector = new FlowSelector { AutomationId = assertionAutomationId },
                        },
                    ],
                },
            },
        };

        return new MauiFlowRunner(driver, new MauiFlowRunnerOptions
        {
            PollTries = 1,
            PollGapMs = 0,
        }).RunWithLegacyAsync(flow);
    }

    /// <summary>A driver that knows exactly one element, so any other selector cannot resolve.</summary>
    private sealed class SingleElementDriver : IMauiFlowDriver
    {
        private readonly ElementInfo _element = new()
        {
            Id = "count-id",
            AutomationId = PresentId,
            Type = "Label",
            IsVisible = true,
            IsEnabled = true,
            Bounds = new BoundsInfo { X = 0, Y = 0, Width = 120, Height = 20 },
        };

        public string? PropertyValue { get; set; }

        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(
                automationId is null || string.Equals(automationId, _element.AutomationId, StringComparison.Ordinal)
                    ? new List<ElementInfo> { _element }
                    : []);

        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult<ElementInfo?>(
                string.Equals(id, _element.Id, StringComparison.Ordinal) ? _element : null);

        public Task<string?> GetPropertyAsync(string elementId, string propertyName)
            => Task.FromResult(PropertyValue);

        public Task<bool> TapAsync(string elementId) => Task.FromResult(true);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });

        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus
        {
            Route = "/home",
            Agent = new AgentDescriptor { InstanceId = "agent-instance" },
            Device = new DeviceDescriptor { Platform = "windows", WindowWidth = 800, WindowHeight = 600 },
            App = new AppDescriptor { PackageId = "com.example.app", Version = "1.0", Build = "42" },
        });
    }
}
