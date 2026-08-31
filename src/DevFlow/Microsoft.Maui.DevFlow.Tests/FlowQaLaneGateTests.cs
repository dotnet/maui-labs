using Xunit;

// The gate source is shared into this project (see the linked FlowQaLaneGate.cs compile item in
// Microsoft.Maui.DevFlow.Tests.csproj) because Arcade never executes the integration-test project
// that owns it. The namespace matches the shared source so the internal gate stays reachable.
namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// The QA lanes used to <c>return</c> early when disabled, which reported a pass for a lane that
/// never ran. These tests pin the replacement gate so a disabled lane is skipped with a reason.
/// </summary>
public class FlowQaLaneGateTests
{
    static Func<string, string?> Env(string variable, string? value)
        => name => string.Equals(name, variable, StringComparison.Ordinal) ? value : null;

    [Fact]
    public void AndroidFlowPilot_RequiresExplicitOptIn()
    {
        var readiness = FlowQaLaneGate.AndroidFlowPilot(Env(FlowQaLaneGate.AndroidFlowPilotVariable, null));

        Assert.False(readiness.IsEnabled);
        Assert.Contains(FlowQaLaneGate.AndroidFlowPilotVariable, readiness.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("1 ")]
    public void AndroidFlowPilot_OnlyExactlyOneEnablesTheLane(string value)
    {
        Assert.False(FlowQaLaneGate.AndroidFlowPilot(Env(FlowQaLaneGate.AndroidFlowPilotVariable, value)).IsEnabled);
    }

    [Fact]
    public void AndroidFlowPilot_EnabledWhenRequested()
    {
        var readiness = FlowQaLaneGate.AndroidFlowPilot(Env(FlowQaLaneGate.AndroidFlowPilotVariable, "1"));

        Assert.True(readiness.IsEnabled);
        Assert.Equal("", readiness.Reason);
    }

    [Fact]
    public void WindowsFlowQa_RequiresWindowsHostEvenWhenRequested()
    {
        var readiness = FlowQaLaneGate.WindowsFlowQa(
            Env(FlowQaLaneGate.WindowsFlowQaVariable, "1"),
            isWindowsHost: false);

        Assert.False(readiness.IsEnabled);
        Assert.Contains("Windows", readiness.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsFlowQa_RequiresOptInEvenOnWindows()
    {
        var readiness = FlowQaLaneGate.WindowsFlowQa(
            Env(FlowQaLaneGate.WindowsFlowQaVariable, null),
            isWindowsHost: true);

        Assert.False(readiness.IsEnabled);
        Assert.Contains(FlowQaLaneGate.WindowsFlowQaVariable, readiness.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsFlowQa_EnabledOnWindowsWhenRequested()
        => Assert.True(
            FlowQaLaneGate.WindowsFlowQa(Env(FlowQaLaneGate.WindowsFlowQaVariable, "1"), isWindowsHost: true)
                .IsEnabled);

    [Fact]
    public void AppKitFlowQa_RequiresMacOSHostEvenWhenRequested()
    {
        var readiness = FlowQaLaneGate.AppKitFlowQa(
            Env(FlowQaLaneGate.AppKitFlowQaVariable, "1"),
            isMacOSHost: false);

        Assert.False(readiness.IsEnabled);
        Assert.Contains("macOS", readiness.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AppKitFlowQa_EnabledOnMacOSWhenRequested()
        => Assert.True(
            FlowQaLaneGate.AppKitFlowQa(Env(FlowQaLaneGate.AppKitFlowQaVariable, "1"), isMacOSHost: true).IsEnabled);

    [Fact]
    public void AppleFlowQa_RequiresMacOSHostEvenWhenRequested()
    {
        var readiness = FlowQaLaneGate.AppleFlowQa(
            Env(FlowQaLaneGate.AppleFlowQaVariable, "1"),
            isMacOSHost: false);

        Assert.False(readiness.IsEnabled);
        Assert.Contains("macOS", readiness.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleFlowQa_EnabledOnMacOSWhenRequested()
        => Assert.True(
            FlowQaLaneGate.AppleFlowQa(Env(FlowQaLaneGate.AppleFlowQaVariable, "1"), isMacOSHost: true).IsEnabled);

    [Fact]
    public void LanesDoNotShareEnvironmentVariables()
    {
        var variables = new[]
        {
            FlowQaLaneGate.AndroidFlowPilotVariable,
            FlowQaLaneGate.WindowsFlowQaVariable,
            FlowQaLaneGate.AppKitFlowQaVariable,
            FlowQaLaneGate.AppleFlowQaVariable,
        };

        Assert.Equal(variables.Length, variables.Distinct(StringComparer.Ordinal).Count());
    }

    // The four attributes are the only part of the gate the suites actually consume. A mis-wire
    // (say WindowsFlowQaFactAttribute asking the AppKit lane) compiles cleanly and then silently
    // skips or silently runs the wrong lane. Two things are pinned below: that the attribute maps
    // readiness to Skip in both directions, and that each attribute's real Skip is one of the
    // reasons its own lane can produce. Comparing the attribute against another call to the same
    // production method under the same environment would pass either way, so it is not used here.

    [Theory]
    [InlineData(typeof(AndroidFlowPilotFactAttribute))]
    [InlineData(typeof(WindowsFlowQaFactAttribute))]
    [InlineData(typeof(AppKitFlowQaFactAttribute))]
    [InlineData(typeof(AppleFlowQaFactAttribute))]
    public void EveryLaneFact_MapsReadinessOntoSkipInBothDirections(Type attributeType)
    {
        var enabled = Construct(attributeType, new FlowQaLaneReadiness(true, ""));
        var disabled = Construct(attributeType, new FlowQaLaneReadiness(false, "lane is off because reasons"));

        Assert.Null(enabled.Skip);
        Assert.Equal("lane is off because reasons", disabled.Skip);
    }

    static FactAttribute Construct(Type attributeType, FlowQaLaneReadiness readiness)
        => (FactAttribute)Activator.CreateInstance(
            attributeType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [readiness],
            culture: null)!;

    [Fact]
    public void AndroidFlowPilotFact_IsWiredToTheAndroidLane()
        => AssertWiredToLane(
            new AndroidFlowPilotFactAttribute().Skip,
            FlowQaLaneGate.AndroidFlowPilot(_ => null));

    [Fact]
    public void WindowsFlowQaFact_IsWiredToTheWindowsLane()
        => AssertWiredToLane(
            new WindowsFlowQaFactAttribute().Skip,
            FlowQaLaneGate.WindowsFlowQa(_ => null, isWindowsHost: false),
            FlowQaLaneGate.WindowsFlowQa(_ => null, isWindowsHost: true));

    [Fact]
    public void AppKitFlowQaFact_IsWiredToTheAppKitLane()
        => AssertWiredToLane(
            new AppKitFlowQaFactAttribute().Skip,
            FlowQaLaneGate.AppKitFlowQa(_ => null, isMacOSHost: false),
            FlowQaLaneGate.AppKitFlowQa(_ => null, isMacOSHost: true));

    [Fact]
    public void AppleFlowQaFact_IsWiredToTheAppleLane()
        => AssertWiredToLane(
            new AppleFlowQaFactAttribute().Skip,
            FlowQaLaneGate.AppleFlowQa(_ => null, isMacOSHost: false),
            FlowQaLaneGate.AppleFlowQa(_ => null, isMacOSHost: true));

    // A null Skip means the lane opted in on this host, which is legitimate and cannot be pinned
    // to a string. Any other value has to be one of the reasons this lane can produce; because
    // EveryDisabledLaneReportsItsOwnReason proves the four lanes' reasons are all distinct, an
    // attribute wired to the wrong lane fails here.
    static void AssertWiredToLane(string? skip, params FlowQaLaneReadiness[] disabledFormsOfThisLane)
    {
        if (skip is null)
            return;

        Assert.Contains(skip, disabledFormsOfThisLane.Select(form => form.Reason));
    }

    // Every lane reason is distinct on every host, which is what makes the four assertions above
    // able to tell the lanes apart rather than all matching the same message.
    [Fact]
    public void EveryDisabledLaneReportsItsOwnReason()
    {
        var reasons = new[]
        {
            FlowQaLaneGate.AndroidFlowPilot(_ => null).Reason,
            FlowQaLaneGate.WindowsFlowQa(_ => null, isWindowsHost: false).Reason,
            FlowQaLaneGate.AppKitFlowQa(_ => null, isMacOSHost: false).Reason,
            FlowQaLaneGate.AppleFlowQa(_ => null, isMacOSHost: false).Reason,
            FlowQaLaneGate.WindowsFlowQa(_ => null, isWindowsHost: true).Reason,
            FlowQaLaneGate.AppKitFlowQa(_ => null, isMacOSHost: true).Reason,
            FlowQaLaneGate.AppleFlowQa(_ => null, isMacOSHost: true).Reason,
        };

        Assert.All(reasons, reason => Assert.NotEqual("", reason));
        Assert.Equal(reasons.Length, reasons.Distinct(StringComparer.Ordinal).Count());
    }
}
