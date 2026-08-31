using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>Explicit broker-owned Shell route checkpoint operations.</summary>
[McpServerToolType]
public sealed class ResumeTools
{
    [McpServerTool(Name = "maui_resume_status"),
     Description("Get the selected app's locally saved route checkpoint. This never navigates the app.")]
    public static Task<string> Status(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is selected)")] int? agentPort = null)
        => Control(session, "status", agentPort);

    [McpServerTool(Name = "maui_resume_save"),
     Description("Explicitly save the authoritative current Shell route for the selected app. This stores no app data or ViewModel state.")]
    public static Task<string> Save(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is selected)")] int? agentPort = null)
        => Control(session, "save", agentPort);

    [McpServerTool(Name = "maui_resume_restore"),
     Description("Explicitly navigate the selected app to its saved Shell route and return restored, failed, or diverged status. It never runs automatically on reconnect.")]
    public static Task<string> Restore(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is selected)")] int? agentPort = null)
        => Control(session, "restore", agentPort);

    [McpServerTool(Name = "maui_resume_clear"),
     Description("Explicitly remove the selected app's locally saved route checkpoint.")]
    public static Task<string> Clear(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is selected)")] int? agentPort = null)
        => Control(session, "clear", agentPort);

    private static async Task<string> Control(McpAgentSession session, string action, int? agentPort)
    {
        try
        {
            var brokerPort = await session.GetBrokerPortAsync();
            var agent = await session.GetSelectedBrokerAgentAsync(agentPort);
            var result = await BrokerClient.ControlCheckpointAsync(brokerPort, agent.Id, action);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
