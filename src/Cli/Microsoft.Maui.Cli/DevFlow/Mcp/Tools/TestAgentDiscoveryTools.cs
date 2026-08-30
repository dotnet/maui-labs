using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>Minimal exact-target discovery for the restricted test-agent MCP profile.</summary>
[McpServerToolType]
public sealed class TestAgentDiscoveryTools
{
    [McpServerTool(Name = "maui_test_agents"),
     System.ComponentModel.Description("List connected targets for the restricted test-agent profile. Copy both agentId and agentInstanceId exactly into every later request; this tool never selects a default target.")]
    public static async Task<string> ListAgents(
        [System.ComponentModel.Description("MCP session injected by the server and used only to query the local broker")] McpAgentSession session)
    {
        var agents = await session.ListAgentsAsync().ConfigureAwait(false) ?? [];
        var targets = agents.Select(agent => new
        {
            agentId = agent.Id,
            agentInstanceId = agent.InstanceId,
            appName = agent.AppName,
            platform = agent.Platform,
            tfm = agent.Tfm,
            version = agent.Version,
        }).ToArray();
        return TestAgentToolSupport.Success(null, new
        {
            profile = "test-agent",
            requiresExplicitTarget = true,
            targets,
        });
    }

    [McpServerTool(Name = "maui_test_status"),
     System.ComponentModel.Description("Return a safe status projection for one explicitly named test-agent target. It does not select a default target or reveal UI text, logs, files, network bodies, screenshots, or source. Supplying an authoring session access request additionally returns the broker-enforced exploration budget still remaining for that session.")]
    public static async Task<string> Status(
        [System.ComponentModel.Description("MCP session injected by the server and used only to resolve the exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Exact stable agent ID and process instance ID returned by maui_test_agents")] MauiTestAgentTarget target,
        [System.ComponentModel.Description("Optional authoring session read request — session id, its read capability, and a complete protocol envelope — whose remaining exploration budget should also be reported")] MauiTestAgentSessionAccessRequest? authoringSession = null)
    {
        var resolved = await TestAgentToolSupport.ResolveTargetAsync(session, target).ConfigureAwait(false);
        if (resolved.Error is not null)
            return TestAgentToolSupport.Failure(null, resolved.Error);

        using var agent = await session.GetTestAgentClientAsync(target).ConfigureAwait(false);
        var status = await agent.GetStatusAsync().ConfigureAwait(false);
        if (status is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Target,
                "The explicit target did not return a status response.",
                retryable: true));
        }

        MauiTestAgentExplorationBudgetState? explorationBudget = null;
        if (authoringSession is not null)
        {
            // The budget is authoring-session state, so reading it needs the session's read
            // capability and a complete envelope, exactly as every other session read does.
            if (string.IsNullOrWhiteSpace(authoringSession.SessionId) ||
                string.IsNullOrWhiteSpace(authoringSession.ReadCapabilityId) ||
                authoringSession.Envelope is null)
            {
                return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.ReadCapabilityRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "Reporting the exploration budget requires the authoring session id, the read capability issued with it, and a complete protocol envelope.",
                    retryable: false));
            }

            var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
            var snapshot = await TestAgentBrokerClient.StatusAsync(brokerPort, authoringSession).ConfigureAwait(false);
            if (snapshot.Value?.Ok != true || snapshot.Value.Snapshot is null)
                return TestAgentToolSupport.BrokerFailure(authoringSession.Envelope.RequestId, snapshot);
            explorationBudget = snapshot.Value.Snapshot.ExplorationBudget;
        }

        return TestAgentToolSupport.Success(null, new
        {
            target = resolved.State,
            running = status.Running,
            app = new
            {
                name = status.App?.Name,
                packageId = status.App?.PackageId,
                version = status.App?.Version,
                build = status.App?.Build,
            },
            device = new
            {
                platform = status.Device?.Platform,
                deviceType = status.Device?.DeviceType,
            },
            route = status.Route,
            window = status.Window,
            explorationBudget,
        });
    }


}

/// <summary>Capabilities for the restricted typed test-authoring and run protocol.</summary>
[McpServerToolType]
public sealed class TestAgentCapabilitiesTool
{
    [McpServerTool(Name = "maui_test_capabilities"),
     System.ComponentModel.Description("Describe the restricted typed test-authoring/run protocol for one explicit target. Mutation requires a human-issued scope-bound grant; pause and continue are reported unsupported rather than simulated.")]
    public static async Task<string> GetCapabilities(
        [System.ComponentModel.Description("MCP session injected by the server and used only to resolve the exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Exact stable agent ID and process instance ID returned by maui_test_agents")] MauiTestAgentTarget target)
    {
        var resolved = await TestAgentToolSupport.ResolveTargetAsync(session, target).ConfigureAwait(false);
        if (resolved.Error is not null)
            return TestAgentToolSupport.Failure(null, resolved.Error);

        using var agent = await session.GetTestAgentClientAsync(target).ConfigureAwait(false);
        var capabilities = await agent.GetCapabilitiesAsync().ConfigureAwait(false);
        var agentSupportsWorkflowLedger = capabilities.ValueKind == System.Text.Json.JsonValueKind.Object &&
            capabilities.TryGetProperty("agent.workflowCommandLedger", out _);
        var nativeHostApprovalAvailable = BrokerClient.HasNativeHostApprovalAuthority();

        return TestAgentToolSupport.Success(null, new
        {
            schema = MauiTestAgentProtocolVersions.Schema,
            policyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
            target = resolved.State,
            requiresExplicitTarget = true,
            requiresReadCapability = true,
            requiresHumanMutationGrant = true,
            nativeHostApprovalAvailable,
            mutationGrantIssuanceAvailable = nativeHostApprovalAvailable,
            supports = new
            {
                authoring = true,
                typedActions = new[] { "tap", "fill", "scroll", "navigate", "back" },
                typedAssertions = new[] { "propEquals", "exists", "notExists", "routeIs", "pageChanged" },
                staticValidation = true,
                liveValidation = true,
                runStart = true,
                runStatus = true,
                runCancel = true,
                runPause = false,
                runContinue = false,
                traceProjection = true,
                deterministicFailureFacts = true,
                inertPatchProposal = true,
                patchApply = false,
                patchApproval = false,
                sourceProposal = false,
                sourceApply = false,
                repairApply = false,
                workflowCommandLedger = agentSupportsWorkflowLedger,
            },
            limitations = nativeHostApprovalAvailable
                ? new[]
                {
                    "Approval still requires a trusted VS Code or Copilot Canvas native host and an explicit human confirmation. Browser and chat text are non-authoritative.",
                }
                : new[]
                {
                    "Native host approval is unavailable for the current broker. Grant-gated mutations fail closed.",
                },
            prohibited = new[]
            {
                "secure-storage", "preferences-mutation", "raw-files", "raw-network-bodies",
                "cdp-evaluation", "cdp-source", "generic-invoke", "extension-call",
                "arbitrary-property-mutation", "shell", "process", "source-apply",
            },
        });
    }
}
