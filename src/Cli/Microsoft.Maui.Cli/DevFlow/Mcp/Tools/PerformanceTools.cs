using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>
/// Performance <em>triage</em> over the agent's existing bounded profiler sampling.
///
/// These tools deliberately do not replace a profiler. They answer "did this interaction allocate,
/// churn the GC, or stall?" and then hand off. Every description says so, because an agent that
/// treats these numbers as profiler-grade attribution will draw wrong conclusions.
/// </summary>
[McpServerToolType]
public sealed class PerformanceTools
{
    private const string TriageContract =
        "This is triage, not profiling: numbers come from the app's own bounded sampling, are perturbed by " +
        "Debug builds, Hot Reload, and the debugger, and cannot attribute cost to call stacks. Frame rate is " +
        "reported only when the platform provides exact native rendered-frame timings — estimated frame rates are never shown, including display-cadence estimates. " +
        "Read 'capability.limitations', 'loss', and 'warnings' before concluding anything, and hand off to a " +
        "native profiler (dotnet-trace, Xcode Instruments, Android Studio Profiler) for real attribution.";

    [McpServerTool(Name = "maui_performance_start"),
     Description(
         "Start a performance triage session on the connected MAUI app and return the initial capability and " +
     "taint report. Preserve session.sessionId and session.stopToken, drive the interaction you care about, " +
     "then pass the id to maui_performance_snapshot and both values to maui_performance_stop. " + TriageContract)]
    public static async Task<string> StartPerformance(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Sampling interval in milliseconds (50-60000); defaults to the agent's configured interval")] int? sampleIntervalMs = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var summary = await agent.StartPerformanceSessionAsync(sampleIntervalMs);
        return CliJson.SerializeUntyped(summary, indented: false);
    }

    [McpServerTool(Name = "maui_performance_snapshot"),
     Description(
         "Summarize the running performance triage session without stopping it: managed, process-footprint, " +
         "and native-heap memory start, " +
         "end, peak and delta for managed, process, and native-heap memory where supported, GC deltas, " +
         "CPU average and peak, thread peak, native frame statistics when " +
         "supported, top hotspots, marker counts, and buffer-loss metadata. " + TriageContract)]
    public static async Task<string> SnapshotPerformance(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Profiler session id returned by maui_performance_start; pass it to avoid reading a replacement session")] string? sessionId = null,
        [Description("Maximum profiler samples to aggregate (1-20000, default: 2000)")] int sampleLimit = 2000,
        [Description("Number of top hotspots to include (1-200, default: 10)")] int hotspotLimit = 10)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var summary = await agent.GetPerformanceSummaryAsync(sessionId, sampleLimit, hotspotLimit);
        return CliJson.SerializeUntyped(summary, indented: false);
    }

    [McpServerTool(Name = "maui_performance_stop"),
     Description(
         "Stop the performance triage session and return the final summary for the recorded window. " + TriageContract)]
    public static async Task<string> StopPerformance(
        McpAgentSession session,
        [Description("Profiler session id returned by maui_performance_start; required so this call never stops another session")] string sessionId,
        [Description("Opaque creator stop token returned by maui_performance_start; required to stop this session")] string stopToken,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Maximum profiler samples to aggregate (1-20000, default: 20000)")] int sampleLimit = 20_000,
        [Description("Number of top hotspots to include (1-200, default: 10)")] int hotspotLimit = 10)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var summary = await agent.StopPerformanceSessionAsync(
            sessionId,
            stopToken,
            sampleLimit,
            hotspotLimit);
        return CliJson.SerializeUntyped(summary, indented: false);
    }
}
