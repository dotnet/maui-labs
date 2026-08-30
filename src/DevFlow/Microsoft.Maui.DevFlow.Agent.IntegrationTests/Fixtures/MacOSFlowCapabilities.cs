using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Explicit AppKit QA capability boundary. These facts describe the experimental fixture only;
/// they never imply Mac Catalyst support or official platform qualification.
/// </summary>
internal static class MacOSFlowCapabilities
{
    internal const string PlatformKey = "macos";
    internal const string Backend = "appkit";

    internal static readonly IReadOnlyList<MacOSFlowCapability> Matrix =
    [
        new("managedNativeIdentity", "required", "Managed AutomationIds must agree with authoritative AppKit accessibility identities."),
        new("shellNavigationWindowModal", "required", "Shell route, window checkpoint, and in-app modal equivalent are verified before replay."),
        new("nativeControls", "required", "The Tier-1 corpus uses AppKit-backed Button and Entry handlers."),
        new("webViewContext", "conditional", "WKWebView/CDP coverage runs only when the agent reports webview capability."),
        new("nativeSystemDialogs", "unsupported", "No stable AppKit NSAlert selector contract is claimed by this fixture."),
        new("multiWindowAutomation", "unsupported", "The fixture has no stable owned multi-window AutomationId contract."),
        new("selectorRepair", "supported-static", "Ambiguity and unknown completion remain fail-closed; no repair is auto-applied."),
        new("sourceProposal", "supported-static", "Build/remap/replay/rollback stays human-approved and AppKit runtime evidence remains experimental."),
        new("trustSecurityReport", "supported-static", "Artifact trust, redaction, and canonical report parity use shared contracts."),
    ];

    internal static PlatformFlowLifecycleException? ValidateStatic(MauiFlow flow, MauiTestPlan plan)
    {
        if (!HasExactPlatform(flow.Platform))
        {
            return PlatformFlowLifecycleException.Capability(
                $"capability-missing: the experimental AppKit host accepts only flows tagged '{PlatformKey}'.");
        }

        if (plan.RequiredPlatforms.Count > 0 &&
            !plan.RequiredPlatforms.Any(static platform =>
                string.Equals(platform, PlatformKey, StringComparison.OrdinalIgnoreCase)))
        {
            return PlatformFlowLifecycleException.Capability(
                $"capability-missing: the AppKit plan must explicitly declare '{PlatformKey}'.");
        }

        return null;
    }

    internal static PlatformFlowLifecycleException? ValidateRuntime(AgentStatus status, MauiTestPlan plan)
    {
        foreach (var requirement in plan.Requirements?.RequiredCapabilities ?? [])
        {
            if (!requirement.Required || IsAvailable(requirement.Name, status.Capabilities))
                continue;

            return PlatformFlowLifecycleException.Capability(
                $"capability-missing: required experimental AppKit capability '{requirement.Name ?? "<unnamed>"}' is unavailable.");
        }

        return null;
    }

    internal static bool IsAvailable(string? capability, AgentCapabilities? available)
        => capability?.Trim() switch
        {
            "agent.ui" => available?.Ui == true,
            "agent.mutations" => available?.Mutations == true,
            "agent.workflowCommandLedger" => available?.WorkflowCommandLedger == true,
            "agent.webview" => available?.WebView == true,
            "agent.theme" => available?.Theme == true,
            _ => false,
        };

    internal static IReadOnlyList<MacOSFlowCapabilityOutcome> Evaluate(AgentStatus? status)
        => Matrix.Select(capability => new MacOSFlowCapabilityOutcome(
            capability.Name,
            capability.State,
            capability.Description,
            capability.Name switch
            {
                "webViewContext" => status?.Capabilities?.WebView == true ? "available" : "capability-missing",
                "nativeSystemDialogs" or "multiWindowAutomation" => "unsupported",
                _ => "covered",
            })).ToArray();

    static bool HasExactPlatform(string? platformTag)
        => !string.IsNullOrWhiteSpace(platformTag) &&
           platformTag.Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Any(static item => string.Equals(item, PlatformKey, StringComparison.OrdinalIgnoreCase));
}

internal sealed record MacOSFlowCapability(string Name, string State, string Description);

internal sealed record MacOSFlowCapabilityOutcome(
    string Name,
    string DeclaredState,
    string Description,
    string Outcome);
