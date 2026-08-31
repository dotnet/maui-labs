using System.ComponentModel;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>
/// One bounded exploration step for the restricted test-agent profile. The tool is deliberately
/// small: it takes a single navigation step and returns the budget the broker has left. It cannot
/// widen its own allowance, because the budget lives in the session plan, is clamped by broker
/// policy, and is counted down by the broker under the same lock that consumes the human-issued
/// exploration grant.
/// </summary>
[McpServerToolType]
public sealed class TestAgentExplorationTool
{
    [McpServerTool(Name = "maui_test_explore"),
     System.ComponentModel.Description("Take exactly one bounded, read-mostly navigation step (tap, scroll, navigate, or back) to discover a path the author could not name. Every step is charged against the session plan's explorationBudget, clamped by broker policy: the broker enforces and counts it down, and exploration stops when maxActions or maxDurationSeconds is spent. It requires a human-approved exploration grant whose scope allows only navigation actions and the 'exploration' side-effect class; an ordinary action grant cannot be spent here, an exploration grant cannot be spent on the ordinary action route, and the authorization it mints dispatches only that one navigation step — same action, same element, same route — never a wider flow. A tap or scroll must carry a selector with a durable key such as an AutomationId and a navigate must carry a route, because a step that cannot be pinned cannot be bound to the budget it spends. It never fills text, asserts, edits the draft, commits, or starts a scenario run, and chat approval is never authorization.")]
    public static async Task<string> Explore(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker and exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Bounded exploration step: a complete envelope naming the authoring session and human-issued exploration grant, the navigation action, and the exploration scope label that the session plan already lists in explorationBudget.allowedScopes")] MauiTestAgentExplorationRequest request)
    {
        if (request?.Envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "An exploration request with a protocol envelope is required.",
                retryable: false));
        }

        var envelope = request.Envelope;
        var action = request.Action?.Trim();
        if (string.IsNullOrEmpty(action) || !MauiTestAgentActions.Exploration.Contains(action))
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.UnsupportedOperation,
                MauiTestAgentErrorCategories.Unsupported,
                "Exploration may only tap, scroll, navigate, or back. Data entry, assertions, drafting, and runs each need their own approval.",
                retryable: false));
        }

        var explorationScope = request.Scope?.Trim();
        if (string.IsNullOrEmpty(explorationScope))
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.ExplorationScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "An exploration step must name one scope from the session plan's explorationBudget.allowedScopes.",
                retryable: false));
        }

        // An exploration step observes; it never records. Appending the discovered step to the
        // draft is a separate draft-change approval, so this request is always execute-only.
        return await TestAgentActionTool.ExecuteAsync(
            session,
            new MauiTestAgentActionRequest
            {
                Envelope = envelope,
                Action = action,
                Selector = request.Selector,
                Route = request.Route,
                DeltaX = request.DeltaX,
                DeltaY = request.DeltaY,
                ItemIndex = request.ItemIndex,
                AppendDraft = false,
                Execute = true,
            },
            explorationScope).ConfigureAwait(false);
    }
}
