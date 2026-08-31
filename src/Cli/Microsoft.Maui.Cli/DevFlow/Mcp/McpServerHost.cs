using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public enum McpServerProfile
{
	Full,
	TestAgent,
}

/// <summary>
/// Raised when a preview MCP profile was requested without its enabling environment variable.
/// It is a caller mistake with a documented remedy, not a fault, so the CLI reports the message
/// and exits non-zero instead of printing a stack trace.
/// </summary>
public sealed class McpProfileDisabledException : InvalidOperationException
{
	public McpProfileDisabledException(string message) : base(message) { }
}

public static class McpServerHost
{
	/// <summary>Environment variable that enables the agent-authoring preview surface.</summary>
	public const string PreviewAgentAuthoringVariable = "DEVFLOW_PREVIEW_AGENT_AUTHORING";

	private static readonly string[] FullToolNames =
	[
		"maui_app_info", "maui_artifact_inspect", "maui_assert", "maui_back", "maui_batch", "maui_battery_info",
		"maui_capabilities", "maui_cdp_evaluate", "maui_cdp_screenshot", "maui_cdp_source",
		"maui_cdp_webviews", "maui_clear", "maui_connectivity", "maui_device_info",
		"maui_display_info", "maui_element", "maui_evidence_capture", "maui_evidence_preview",
		"maui_extension_call", "maui_extension_list", "maui_files_delete", "maui_files_download",
		"maui_files_list", "maui_files_upload", "maui_fill", "maui_flow_list", "maui_flow_record_cancel",
		"maui_flow_record_start", "maui_flow_record_status", "maui_flow_record_step", "maui_flow_record_stop",
		"maui_flow_replay", "maui_flow_validate", "maui_focus", "maui_geolocation", "maui_get_property",
		"maui_get_theme", "maui_gesture", "maui_hittest", "maui_invoke_action", "maui_jobs_list",
		"maui_jobs_run", "maui_key", "maui_list_actions", "maui_list_agents",
		"maui_logs", "maui_navigate", "maui_network", "maui_network_clear", "maui_network_detail",
		"maui_performance_snapshot", "maui_performance_start", "maui_performance_stop", "maui_preferences_clear",
		"maui_preferences_delete", "maui_preferences_get", "maui_preferences_list", "maui_preferences_set",
		"maui_problems", "maui_problems_clear", "maui_query", "maui_query_css", "maui_recording_start",
		"maui_recording_status", "maui_recording_stop", "maui_resize", "maui_resume_clear", "maui_resume_restore",
		"maui_resume_save", "maui_resume_status", "maui_screenshot", "maui_scroll", "maui_secure_storage_clear",
		"maui_secure_storage_delete", "maui_secure_storage_get", "maui_secure_storage_set", "maui_select_agent",
		"maui_sensors_list", "maui_sensors_start", "maui_sensors_stop", "maui_set_property", "maui_set_theme",
		"maui_status", "maui_storage_roots", "maui_tap", "maui_tree", "maui_wait",
	];

	/// <summary>
	/// The exact tool types registered on the restricted test-agent server, in registration order.
	/// </summary>
	/// <remarks>
	/// This is the authoritative record of what the profile serves. The server still registers each
	/// one through the generic <c>WithTools&lt;T&gt;()</c> overload so the builder keeps its
	/// trim-safe static shape, so the two cannot be wired together at run time. They are
	/// tied together by test instead: one assertion compares this list against the
	/// <c>WithTools&lt;T&gt;()</c> calls in this file's source, and another reflects the
	/// <c>[McpServerTool]</c> names off these types and compares them with
	/// <see cref="TestAgentToolNames"/>. Adding or removing either a registered type or a tool
	/// method therefore fails the build's tests rather than silently widening the surface.
	/// </remarks>
	internal static readonly Type[] TestAgentToolTypes =
	[
		typeof(TestAgentDiscoveryTools),
		typeof(TestAgentCapabilitiesTool),
		typeof(TestAgentAuthoringTool),
		typeof(TestAgentActionTool),
		typeof(TestAgentExplorationTool),
		typeof(TestAgentAssertionTool),
		typeof(TestAgentValidationTool),
		typeof(TestAgentRunTool),
		typeof(TestAgentTraceTool),
		typeof(TestAgentFailureTool),
		typeof(TestAgentPatchTool),
		typeof(TestAgentImprovementsTool),
	];

	private static readonly string[] TestAgentToolNames =
	[
		"maui_test_action",
		"maui_test_agents",
		"maui_test_assertion",
		"maui_test_author",
		"maui_test_capabilities",
		"maui_test_explore",
		"maui_test_failure",
		"maui_test_improvements",
		"maui_test_patch",
		"maui_test_run",
		"maui_test_status",
		"maui_test_trace",
		"maui_test_validate",
	];

	public static bool TryParseProfile(string? value, out McpServerProfile profile)
	{
		switch (value?.Trim().ToLowerInvariant())
		{
			case "full":
				profile = McpServerProfile.Full;
				return true;
			case "test-agent":
				profile = McpServerProfile.TestAgent;
				return true;
			default:
				profile = default;
				return false;
		}
	}

	/// <summary>Returns the exact MCP tool inventory used by a profile for testable policy review.</summary>
	public static bool IsProfileEnabled(McpServerProfile profile)
		=> IsProfileEnabled(profile, MauiPreviewFeatureFlagConfiguration.FromEnvironment());

	internal static bool IsProfileEnabled(
		McpServerProfile profile,
		MauiPreviewFeatureFlags previewFlags)
	{
		ArgumentNullException.ThrowIfNull(previewFlags);
		return profile switch
		{
			McpServerProfile.Full => true,
			McpServerProfile.TestAgent =>
				DevFlowPreviewPolicy.IsAgentAuthoringEnabled(previewFlags) &&
				!previewFlags.AutoApplyRepair &&
				!previewFlags.AutoApplySource &&
				!previewFlags.ModelProviderEnabled &&
				!previewFlags.TelemetryEgressEnabled,
			_ => false,
		};
	}

	public static IReadOnlyList<string> GetToolInventory(McpServerProfile profile)
		=> GetToolInventory(profile, MauiPreviewFeatureFlagConfiguration.FromEnvironment());

	internal static IReadOnlyList<string> GetToolInventory(
		McpServerProfile profile,
		MauiPreviewFeatureFlags previewFlags)
	{
		if (profile is not McpServerProfile.Full and not McpServerProfile.TestAgent)
			throw new ArgumentOutOfRangeException(nameof(profile));
		if (!IsProfileEnabled(profile, previewFlags))
			return [];

		return profile switch
		{
			McpServerProfile.Full => FullToolNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
			McpServerProfile.TestAgent => TestAgentToolNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
			_ => throw new ArgumentOutOfRangeException(nameof(profile)),
		};
	}

	public static Task RunAsync() => RunAsync(McpServerProfile.Full);

	public static Task RunAsync(McpServerProfile profile)
		=> RunAsync(profile, MauiPreviewFeatureFlagConfiguration.FromEnvironment());

	internal static async Task RunAsync(
		McpServerProfile profile,
		MauiPreviewFeatureFlags previewFlags)
	{
		ArgumentNullException.ThrowIfNull(previewFlags);
		if (profile is not McpServerProfile.Full and not McpServerProfile.TestAgent)
			throw new ArgumentOutOfRangeException(nameof(profile));
		if (!IsProfileEnabled(profile, previewFlags))
		{
			throw new McpProfileDisabledException(
				"The test-agent MCP profile is a preview surface and is disabled by default. " +
				"Set the environment variable " + PreviewAgentAuthoringVariable + "=true and run the command again. " +
				"PowerShell: $env:" + PreviewAgentAuthoringVariable + " = 'true'; " +
				"bash: export " + PreviewAgentAuthoringVariable + "=true. " +
				"Use --profile full for the unrestricted profile, which needs no flag.");
		}

		var version = typeof(McpServerHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

		var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

		// The MCP server uses stdio transport (stdin/stdout for JSON-RPC).
		// The default console logger writes to stdout, corrupting the protocol stream.
		// Redirect all logging to stderr so diagnostics are preserved without pollution.
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

		builder.Services.AddSingleton<McpAgentSession>();

		if (profile == McpServerProfile.TestAgent)
		{
			builder.Services
				.AddMcpServer(options =>
				{
					options.ServerInfo = new() { Name = "maui-test-agent", Version = version };
				})
				.WithStdioServerTransport()
				.WithTools<TestAgentDiscoveryTools>()
				.WithTools<TestAgentCapabilitiesTool>()
				.WithTools<TestAgentAuthoringTool>()
				.WithTools<TestAgentActionTool>()
				.WithTools<TestAgentExplorationTool>()
				.WithTools<TestAgentAssertionTool>()
				.WithTools<TestAgentValidationTool>()
				.WithTools<TestAgentRunTool>()
				.WithTools<TestAgentTraceTool>()
				.WithTools<TestAgentFailureTool>()
				.WithTools<TestAgentPatchTool>()
				.WithTools<TestAgentImprovementsTool>();
		}
		else if (profile == McpServerProfile.Full)
		{
			builder.Services
				.AddMcpServer(options =>
				{
					options.ServerInfo = new() { Name = "maui", Version = version };
				})
				.WithStdioServerTransport()
				.WithTools<ScreenshotTool>()
				.WithTools<TreeTool>()
				.WithTools<LogsTool>()
				.WithTools<NetworkTool>()
				.WithTools<InteractionTools>()
				.WithTools<PropertyTools>()
				.WithTools<DiagnosticsTools>()
				.WithTools<PerformanceTools>()
				.WithTools<EvidenceTools>()
				.WithTools<ResumeTools>()
				.WithTools<NavigationTools>()
				.WithTools<QueryTools>()
				.WithTools<AgentTools>()
				.WithTools<CdpTools>()
				.WithTools<AssertTool>()
				.WithTools<RecordingTools>()
				.WithTools<PreferencesTools>()
				.WithTools<PlatformTools>()
				.WithTools<ThemeTools>()
				.WithTools<SensorTools>()
				.WithTools<JobTools>()
				.WithTools<FileTools>()
				.WithTools<BatchTools>()
				.WithTools<InvokeTools>()
				.WithTools<ExtensionTools>()
				.WithTools<ArtifactTools>()
				.WithTools<Flows.FlowTools>()
				.WithTools<Flows.FlowRecordTools>()
				.WithResources<McpAppResources>();
		}
		else
		{
			throw new ArgumentOutOfRangeException(nameof(profile));
		}

		await builder.Build().RunAsync();
	}
}
