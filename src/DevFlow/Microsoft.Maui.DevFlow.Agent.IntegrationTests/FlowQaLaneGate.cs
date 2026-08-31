using Xunit;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>Opt-in state of one environment-gated QA lane, with the reason it is not running.</summary>
internal sealed record FlowQaLaneReadiness(bool IsEnabled, string Reason);

/// <summary>
/// Deterministic opt-in evaluation for the environment-gated QA lanes. These lanes build and
/// launch a real app, so they cannot run everywhere; reporting a skip reason instead of
/// returning early keeps a disabled lane visibly skipped rather than reporting a pass for work
/// it never did.
/// </summary>
internal static class FlowQaLaneGate
{
    internal const string AndroidFlowPilotVariable = "DEVFLOW_RUN_ANDROID_FLOW_PILOT";
    internal const string WindowsFlowQaVariable = "DEVFLOW_RUN_WINDOWS_FLOW_QA";
    internal const string AppKitFlowQaVariable = "DEVFLOW_RUN_APPKIT_FLOW_QA";
    internal const string AppleFlowQaVariable = "DEVFLOW_RUN_APPLE_FLOW_QA";

    internal static FlowQaLaneReadiness AndroidFlowPilot(Func<string, string?>? readEnvironment = null)
        => Evaluate(
            AndroidFlowPilotVariable,
            "Android flow pilot not requested.",
            hostSupported: true,
            hostRequirement: null,
            readEnvironment);

    internal static FlowQaLaneReadiness WindowsFlowQa(
        Func<string, string?>? readEnvironment = null,
        bool? isWindowsHost = null)
        => Evaluate(
            WindowsFlowQaVariable,
            "Windows flow QA not requested.",
            hostSupported: isWindowsHost ?? OperatingSystem.IsWindows(),
            hostRequirement: "Windows flow QA requires a Windows MAUI host.",
            readEnvironment);

    internal static FlowQaLaneReadiness AppKitFlowQa(
        Func<string, string?>? readEnvironment = null,
        bool? isMacOSHost = null)
        => Evaluate(
            AppKitFlowQaVariable,
            "Experimental AppKit flow QA not requested.",
            hostSupported: isMacOSHost ?? OperatingSystem.IsMacOS(),
            hostRequirement: "Experimental AppKit flow QA requires a macOS host.",
            readEnvironment);

    internal static FlowQaLaneReadiness AppleFlowQa(
        Func<string, string?>? readEnvironment = null,
        bool? isMacOSHost = null)
        => Evaluate(
            AppleFlowQaVariable,
            "Apple flow QA not requested.",
            hostSupported: isMacOSHost ?? OperatingSystem.IsMacOS(),
            hostRequirement: "Apple flow QA requires a macOS host.",
            readEnvironment);

    private static FlowQaLaneReadiness Evaluate(
        string variable,
        string requirement,
        bool hostSupported,
        string? hostRequirement,
        Func<string, string?>? readEnvironment)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        if (!hostSupported)
            return new FlowQaLaneReadiness(false, hostRequirement ?? $"{variable}=1 requires a supported host.");

        return string.Equals(readEnvironment(variable), "1", StringComparison.Ordinal)
            ? new FlowQaLaneReadiness(true, "")
            : new FlowQaLaneReadiness(false, $"{requirement} Set {variable}=1 to run this lane.");
    }
}

/// <summary>A fact that runs only when the Android flow-pilot lane is explicitly requested.</summary>
/// <remarks>
/// Readiness is evaluated in the constructor, so xUnit decides to skip at discovery time rather
/// than when the test runs. A fixture that mutated the lane variable after discovery could no
/// longer influence it; the lane variables are expected to be set before the process starts.
/// </remarks>
public sealed class AndroidFlowPilotFactAttribute : FactAttribute
{
    public AndroidFlowPilotFactAttribute()
        : this(FlowQaLaneGate.AndroidFlowPilot())
    {
    }

    internal AndroidFlowPilotFactAttribute(FlowQaLaneReadiness readiness)
    {
        if (!readiness.IsEnabled)
            Skip = readiness.Reason;
    }
}

/// <summary>A fact that runs only on a Windows host with the Windows flow-QA lane requested.</summary>
/// <remarks>Readiness is evaluated at discovery time; see <see cref="AndroidFlowPilotFactAttribute"/>.</remarks>
public sealed class WindowsFlowQaFactAttribute : FactAttribute
{
    public WindowsFlowQaFactAttribute()
        : this(FlowQaLaneGate.WindowsFlowQa())
    {
    }

    internal WindowsFlowQaFactAttribute(FlowQaLaneReadiness readiness)
    {
        if (!readiness.IsEnabled)
            Skip = readiness.Reason;
    }
}

/// <summary>A fact that runs only on a macOS host with the experimental AppKit lane requested.</summary>
/// <remarks>Readiness is evaluated at discovery time; see <see cref="AndroidFlowPilotFactAttribute"/>.</remarks>
public sealed class AppKitFlowQaFactAttribute : FactAttribute
{
    public AppKitFlowQaFactAttribute()
        : this(FlowQaLaneGate.AppKitFlowQa())
    {
    }

    internal AppKitFlowQaFactAttribute(FlowQaLaneReadiness readiness)
    {
        if (!readiness.IsEnabled)
            Skip = readiness.Reason;
    }
}

/// <summary>A fact that runs only on a macOS host with the Apple flow-QA lane requested.</summary>
/// <remarks>Readiness is evaluated at discovery time; see <see cref="AndroidFlowPilotFactAttribute"/>.</remarks>
public sealed class AppleFlowQaFactAttribute : FactAttribute
{
    public AppleFlowQaFactAttribute()
        : this(FlowQaLaneGate.AppleFlowQa())
    {
    }

    internal AppleFlowQaFactAttribute(FlowQaLaneReadiness readiness)
    {
        if (!readiness.IsEnabled)
            Skip = readiness.Reason;
    }
}
