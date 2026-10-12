using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class MutationLeaseTools
{
    [McpServerTool(Name = "maui_control_status"),
     Description("Inspect who currently holds the app-wide DevFlow mutation lease. Use before attempting to take control from another Inspector, MCP client, or CLI session.")]
    public static async Task<string> Status(
        McpAgentSession session,
        [DescriptionAttribute("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.ControlMutationLeaseAsync("status");
        return SerializeStatus(status, action: "status", forceRequested: false);
    }

    [McpServerTool(Name = "maui_take_control"),
     Description("Claim the app-wide DevFlow mutation lease for this MCP session. Mutating tools already claim it automatically when free. Use force=true only after the user approves interrupting the current holder. Leases expire after about 10 seconds without activity, and each mutation refreshes or reacquires this session's lease.")]
    public static async Task<string> TakeControl(
        McpAgentSession session,
        [DescriptionAttribute("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [DescriptionAttribute("Force takeover from the current lease holder. Use only after explicit user approval.")] bool force = false)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.ControlMutationLeaseAsync("claim", force);
        return SerializeStatus(status, action: "claim", force);
    }

    [McpServerTool(Name = "maui_release_control"),
     Description("Release this MCP session's DevFlow mutation lease so an Inspector or another automation session can drive the app.")]
    public static async Task<string> ReleaseControl(
        McpAgentSession session,
        [DescriptionAttribute("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.ControlMutationLeaseAsync("release");
        return SerializeStatus(status, action: "release", forceRequested: false);
    }

    private static string SerializeStatus(
        MutationLeaseStatus status,
        string action,
        bool forceRequested)
    {
        var blocked = status.HeldByOther && !status.YouHold;
        var holder = status.Label ?? status.HolderKind ?? "another DevFlow session";
        var releaseBlocked = blocked && action == "release";
        var nextStep = blocked && !releaseBlocked
            ? forceRequested
                ? "An in-flight mutation may still be completing. Poll maui_control_status, then retry maui_take_control with force=true."
                : "Ask the user for approval, then call maui_take_control with force=true and retry the mutation."
            : null;
        var enforced = !string.Equals(status.Authority, "unsupported", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(status.Authority, "disabled", StringComparison.OrdinalIgnoreCase);
        return CliJson.SerializeUntyped(new JsonObject
        {
            ["ok"] = status.Ok,
            ["allowed"] = status.Allowed,
            ["enforced"] = enforced,
            ["youHold"] = enforced && status.YouHold,
            ["heldByOther"] = enforced && status.HeldByOther,
            ["leaseId"] = status.LeaseId,
            ["holderKind"] = status.HolderKind,
            ["label"] = status.Label,
            ["expiresInMs"] = status.ExpiresInMs,
            ["authority"] = status.Authority,
            ["error"] = status.Error ?? (releaseBlocked
                ? "This MCP session does not hold the active mutation lease; nothing was released."
                : blocked
                    ? $"{holder} is currently driving the app."
                    : null),
            ["nextStep"] = nextStep,
            ["message"] = enforced ? null : "This agent does not enforce mutation leases; mutating tools may proceed without taking control.",
        }, indented: false);
    }
}
