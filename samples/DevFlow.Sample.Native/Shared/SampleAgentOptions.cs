using Microsoft.Maui.DevFlow.Agent.Core;

namespace DevFlow.Sample.Native;

/// <summary>
/// Builds the <see cref="AgentOptions"/> every native sample head boots the agent with.
///
/// The integration suite picks a free port per run and passes it to the app in DEVFLOW_TEST_PORT so
/// several sample heads can run side by side without fighting over the default port. This mirrors
/// <c>MauiProgram.ResolveAgentPort</c> in the MAUI sample, which is what keeps the shared test
/// fixtures working against both frameworks.
///
/// When the variable is absent the default port is kept, which deliberately leaves the build-injected
/// <c>MauiDevFlowPort</c> metadata in charge — <see cref="DevFlowAgentHost.Configure"/> only falls back
/// to that metadata when the port was never customised.
/// </summary>
internal static class SampleAgentOptions
{
    public static AgentOptions Create() => new()
    {
        Port = ResolvePort(),

        // The MAUI sample enables the profiler too; the profiler endpoints are framework-neutral,
        // so the shared ProfilerTests run against both frameworks and need it switched on.
        EnableProfiler = true,
    };

    static int ResolvePort()
        => int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var port) && port > 0
            ? port
            : AgentOptions.DefaultPort;
}
