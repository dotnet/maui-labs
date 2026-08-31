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

    /// <summary>
    /// Deliberately pre-capability, in the same authority tier as <c>maui_test_agents</c>,
    /// <c>maui_test_status</c>, and <c>maui_test_capabilities</c>: an exact target is required, an
    /// authoring session read capability is not.
    ///
    /// The dividing line in this profile is whether a tool reads broker-owned authoring session
    /// state. <c>maui_test_improvements</c> does — it analyses the session's draft plan and flow —
    /// so it takes a complete envelope with the session's read capability. This tool reads nothing
    /// but the live app's structure, exactly as <c>maui_test_status</c> does. Requiring an envelope
    /// here would mean an authoring draft has to exist before a read-only structural scan can run,
    /// which makes a pure read create broker session state as a side effect and blocks the
    /// conversational "look at this screen first, decide what to test after" flow the skill
    /// describes.
    ///
    /// What keeps that safe is the projection, not a capability: the scan runs with evidence off,
    /// suppression matching off, and text privacy at its only accepted value, and the response
    /// carries no source paths, control text or values, raw evidence, policy reasons, screenshots,
    /// logs, network data, system evidence, or mutation authority.
    /// </summary>
    [McpServerTool(Name = "maui_test_layout_diagnostics"),
     System.ComponentModel.Description("Run a bounded, read-only layout scan against one exact test-agent target. Returns structural rule outcomes and coverage without source paths, control text or values, screenshots, logs, policy reasons, or mutation authority. Deliberately pre-capability like maui_test_status: it reads no authoring session, so it needs an exact target but no session read capability.")]
    public static async Task<string> LayoutDiagnostics(
        [System.ComponentModel.Description("MCP session injected by the server and used only to resolve the exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Exact stable agent ID and process instance ID returned by maui_test_agents")] MauiTestAgentTarget target,
        [System.ComponentModel.Description("Optional exact live element ID; when omitted the active page is scanned")] string? elementId = null,
        [System.ComponentModel.Description("Element budget from 1 through 2000; defaults to 500")] int maxElements = 500)
    {
        if (maxElements is < 1 or > 2000)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "maxElements must be between 1 and 2000.",
                retryable: false));
        }

        var resolved = await TestAgentToolSupport.ResolveTargetAsync(session, target).ConfigureAwait(false);
        if (resolved.Error is not null)
            return TestAgentToolSupport.Failure(null, resolved.Error);

        using var agent = await session.GetTestAgentClientAsync(target).ConfigureAwait(false);
        LayoutInspectionResult? report;
        try
        {
            report = await agent.AnalyzeLayoutAsync(new LayoutInspectionRequest
            {
                Profile = "agent",
                Scope = new LayoutInspectionScope
                {
                    Mode = LayoutScopeModes.ActivePage,
                    RootElementId = string.IsNullOrWhiteSpace(elementId) ? null : elementId,
                    IncludeNativeElements = true,
                    IncludeBlazorElements = true,
                },
                MaxElements = maxElements,
                IncludeEvidence = false,
                Privacy = new LayoutPrivacyOptions { Text = "none" },
                SuppressionMode = LayoutSuppressionModes.Off,
                Suppressions = [],
            }).ConfigureAwait(false);
        }
        catch (LayoutDiagnosticsException ex)
        {
            return TestAgentToolSupport.Failure(null, DescribeLayoutFailure(ex));
        }

        if (report is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Capability,
                "The exact target does not support layout diagnostics.",
                retryable: false));
        }

        var findings = report.Findings.Take(100).Select(finding => new
        {
            finding.Id,
            finding.RuleId,
            finding.Outcome,
            finding.Severity,
            finding.Confidence,
            element = finding.Element is null ? null : new
            {
                finding.Element.Id,
                finding.Element.Type,
                finding.Element.AutomationId,
            },
        }).ToArray();
        return TestAgentToolSupport.Success(null, new
        {
            target = resolved.State,
            report.SchemaVersion,
            report.RuleSetVersion,
            report.Snapshot.Stable,
            report.Snapshot.StabilityReason,
            scope = new
            {
                report.Scope.RootElementId,
                report.Scope.Window,
                report.Scope.ElementsExamined,
                report.Scope.Truncated,
            },
            report.Summary,
            coverage = new
            {
                report.Coverage.Overall,
                report.Coverage.Rules,
                report.Coverage.Limitations,
            },
            findings,
            findingsTruncated = report.Findings.Count > findings.Length,
            // The tier is stated in the response so an agent, a reviewer, and a test all read the
            // same decision instead of inferring it from the parameter list.
            readAuthority = new
            {
                tier = "pre-capability-discovery",
                requiresExplicitTarget = true,
                requiresSessionReadCapability = false,
                readsAuthoringSession = false,
                peers = new[] { "maui_test_agents", "maui_test_status", "maui_test_capabilities" },
                rationale =
                    "This tool reads only the live app's structure, so it needs no authoring session. " +
                    "maui_test_improvements requires an envelope because it reads the session's draft plan and flow.",
            },
            omissions = new[]
            {
                "source-paths", "control-text", "control-values", "raw-evidence",
                "policy-reasons", "screenshots", "logs", "network", "system-evidence",
                "authoring-session-state", "mutation-authority",
            },
        });
    }

    /// <summary>
    /// Maps a refused or unreachable layout scan to a typed error instead of letting the exception
    /// escape the tool.
    ///
    /// A version or contract mismatch with an older agent is an ordinary, expected answer here: the
    /// agent ships inside the app under inspection and is updated independently of this server. An
    /// escaping exception gives the caller a stack trace it cannot classify, retry correctly, or
    /// report to a human; a typed failure names the reason and whether retrying can help.
    ///
    /// <para>The agent's own text is <b>not</b> a trusted string in this profile. Whatever produced
    /// it — an old agent, a proxy, a transport stack, or something impersonating an agent — it is
    /// unbounded, attacker-influenceable, and can carry exactly the control text, values, and paths
    /// this tool exists to withhold. So every category below emits a fixed message, and the one
    /// case that carries a detail rebuilds that detail from a fully matched, tightly shaped refusal
    /// rather than echoing what arrived.</para>
    /// </summary>
    internal static MauiTestAgentError DescribeLayoutFailure(LayoutDiagnosticsException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The scope element the caller named is gone. That is the caller's own request being wrong
        // about the live tree — a page changed, an item scrolled out of realization, the id was
        // stale — not a capability gap and not an unreachable target, so it is classified as an
        // invalid request and named plainly enough that the caller can re-resolve the element.
        //
        // It is classified on the machine-readable reason code, never on the agent's message: the
        // reason is a fixed token this server compares against a constant, while the message is
        // agent-authored text that carries the element id the caller supplied and, on some agents,
        // more of the tree than that. Nothing from either string is echoed.
        if (string.Equals(
            exception.ErrorType,
            LayoutDiagnosticsErrorTypes.ElementNotFound,
            StringComparison.Ordinal))
        {
            return TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The element supplied to the layout scan no longer exists in the exact target's " +
                    "live tree. Re-resolve it from a fresh maui_test_explore result, or omit it to " +
                    "scan the active page.",
                retryable: false);
        }

        // The refusal an agent older than this server gives when it is asked for a payload version
        // it does not know. That is a capability gap, not a malformed request from the caller, and
        // retrying cannot change it.
        if (exception.StatusCode == 400 &&
            TryDescribeSchemaVersionRefusal(exception.Message, out var detail))
        {
            return TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.UnsupportedOperation,
                MauiTestAgentErrorCategories.Capability,
                "The exact target does not support this layout diagnostics payload version. " +
                    $"It requires: {detail}",
                retryable: false);
        }

        if (exception.StatusCode == 400)
        {
            return TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The exact target rejected the layout diagnostics request as invalid. " +
                    "The target's own message is withheld from this restricted profile; " +
                    "ask the operator to run 'maui devflow diagnostics layout' against the same " +
                    "app to see it.",
                retryable: false);
        }

        // StatusCode 0 is the Driver's marker for a request that never produced an HTTP answer.
        if (exception.StatusCode <= 0)
        {
            return TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Capability,
                "The exact target could not be reached for a layout diagnostics scan.",
                retryable: exception.Retryable);
        }

        // The status code is a bounded integer produced by the HTTP stack, never agent-authored
        // text, so it is the one detail an unknown failure can carry safely.
        return TestAgentToolSupport.Error(
            MauiTestAgentErrorCodes.TargetUnavailable,
            MauiTestAgentErrorCategories.Capability,
            "The exact target could not complete the layout diagnostics request " +
                $"(HTTP {exception.StatusCode}).",
            retryable: exception.Retryable);
    }

    /// <summary>Upper bound on any agent-derived detail this tool is willing to surface.</summary>
    internal const int MaxLayoutFailureDetailChars = 200;

    /// <summary>Longest version token a recognized refusal may name.</summary>
    private const int MaxSchemaVersionChars = 16;

    private const string SchemaRefusalPrefix = "schemaVersion must be '";
    private const string SchemaRefusalSuffix = "'.";

    /// <summary>
    /// Recognizes the exact refusal a DevFlow agent emits for an unknown layout payload version:
    /// <c>schemaVersion must be '&lt;version&gt;'.</c>
    ///
    /// <para>The whole message must match that shape, and the only variable part is a short
    /// version token of digits and dots. That is what keeps classification off arbitrary substring
    /// injection: a hostile or merely verbose message cannot reach this branch by embedding
    /// <c>schemaVersion</c> somewhere inside itself, and the detail handed back is rebuilt from the
    /// validated token rather than copied out of the incoming string.</para>
    /// </summary>
    private static bool TryDescribeSchemaVersionRefusal(string? message, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrEmpty(message))
            return false;
        // Bound the work before any scanning: a legitimate refusal of this shape can never be
        // longer than its fixed text plus a short version token, and it must be longer than the
        // fixed text alone. Without the lower bound the prefix and suffix can overlap on a
        // 24-character message — `schemaVersion must be '.` satisfies both — and the slice below
        // computes a negative length and throws straight back out of the tool.
        if (message.Length <= SchemaRefusalPrefix.Length + SchemaRefusalSuffix.Length ||
            message.Length > SchemaRefusalPrefix.Length + MaxSchemaVersionChars + SchemaRefusalSuffix.Length)
        {
            return false;
        }
        if (!message.StartsWith(SchemaRefusalPrefix, StringComparison.OrdinalIgnoreCase) ||
            !message.EndsWith(SchemaRefusalSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var version = message[SchemaRefusalPrefix.Length..^SchemaRefusalSuffix.Length];
        if (version.Length == 0 || version.Length > MaxSchemaVersionChars)
            return false;
        foreach (var character in version)
        {
            if (!char.IsAsciiDigit(character) && character != '.')
                return false;
        }

        detail = $"schemaVersion '{version}'";
        // Belt and braces: the shape above already bounds this, and the cap states the contract
        // the hostile-input tests hold this function to.
        return detail.Length <= MaxLayoutFailureDetailChars;
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
