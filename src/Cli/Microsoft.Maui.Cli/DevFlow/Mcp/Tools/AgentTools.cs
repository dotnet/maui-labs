using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class AgentTools
{
    [McpServerTool(Name = "maui_list_agents"), Description("List all connected MAUI DevFlow agents (running apps). Shows app name, platform, port, and uptime.")]
    public static async Task<string> ListAgents(McpAgentSession session)
    {
        var agents = await session.ListAgentsAsync();
        if (agents == null || agents.Length == 0)
            return "No agents connected. Build and run a MAUI app with Microsoft.Maui.DevFlow.Agent configured.";

        var result = new JsonArray();
        foreach (var agent in agents)
        {
            result.Add((JsonNode)new JsonObject
            {
                ["id"] = agent.Id,
                ["appName"] = agent.AppName,
                ["platform"] = agent.Platform,
                ["tfm"] = agent.Tfm,
                ["port"] = agent.Port,
                ["version"] = agent.Version,
                ["uptime"] = (DateTime.UtcNow - agent.ConnectedAt).ToString(@"hh\:mm\:ss")
            });
        }

        return CliJson.SerializeUntyped(result, indented: false);
    }

    [McpServerTool(Name = "maui_status"), Description("Get detailed status of a connected MAUI DevFlow agent including platform, device type, app name, and version.")]
    public static async Task<string> Status(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Window index for multi-window apps")] int? window = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.GetStatusAsync(window);
        if (status == null)
            return "Agent not responding. Is the app running?";

        return CliJson.SerializeUntyped(status, indented: false);
    }

    [McpServerTool(Name = "maui_wait"), Description("Wait for a MAUI DevFlow agent to connect. Blocks until an agent registers with the broker or timeout is reached.")]
    public static async Task<string> Wait(
        McpAgentSession session,
        [Description("Timeout in seconds (default: 30)")] int timeout = 30,
        [Description("Wait for a specific app name")] string? app = null)
    {
        var brokerPort = await session.GetBrokerPortAsync();
        var deadline = DateTime.UtcNow.AddSeconds(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var agents = await BrokerClient.ListAgentsAsync(brokerPort);
            if (agents != null && agents.Length > 0)
            {
                var match = app != null
                    ? agents.FirstOrDefault(a => a.AppName?.Contains(app, StringComparison.OrdinalIgnoreCase) == true)
                    : agents.FirstOrDefault();

                if (match != null)
                {
                    session.DefaultAgentPort = match.Port;
                    return CliJson.SerializeUntyped(new JsonObject
                    {
                        ["id"] = match.Id,
                        ["appName"] = match.AppName,
                        ["platform"] = match.Platform,
                        ["tfm"] = match.Tfm,
                        ["port"] = match.Port,
                        ["version"] = match.Version
                    }, indented: false);
                }
            }

            await Task.Delay(500);
        }

        return $"Timeout after {timeout}s — no agent connected" + (app != null ? $" matching '{app}'" : "") + ".";
    }

    [McpServerTool(Name = "maui_select_agent"), Description("Set the default agent for this MCP session. Subsequent tool calls will use this agent automatically without needing agentPort.")]
    public static string SelectAgent(
        McpAgentSession session,
        [Description("Agent HTTP port to use as default")] int agentPort)
    {
        session.DefaultAgentPort = agentPort;
        return $"Default agent set to port {agentPort}. All subsequent commands will use this agent.";
    }
}
