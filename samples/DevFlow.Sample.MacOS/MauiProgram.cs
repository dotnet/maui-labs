#if DEBUG && DEVFLOW_INTEGRATION_TEST
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

namespace DevFlow.Sample;

public static partial class MauiProgram
{
	static int ResolveAgentPort()
		=> int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var envPort)
			? envPort
			: 9223;

#if DEBUG && DEVFLOW_INTEGRATION_TEST
	static void ConfigureIntegrationTestExtension(AgentOptions options, IntegrationTestState state)
	{
		var testControl = options.RegisterExtension(
			"com.example.devflow.integrationtest",
			"Deterministic AppKit fixture lifecycle support for the integration-test build only.",
			"1.0.0",
			["seed", "state-fingerprint"]);

		testControl.MapTool(
			"seed",
			"Resets the in-memory AppKit fixture data to the deterministic integration-test seed.",
			"POST",
			"seed",
			async request =>
			{
				string? seedId = null;
				using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body))
				{
					if (document.RootElement.TryGetProperty("seedId", out var seedProperty) &&
						seedProperty.ValueKind == JsonValueKind.String)
					{
						seedId = seedProperty.GetString();
					}
				}

				var snapshot = await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(
					() => state.ApplySeed(seedId, Shell.Current?.CurrentState?.Location?.ToString()));
				return HttpResponse.Json(ToIntegrationTestStateResponse(snapshot));
			},
			parameters: JsonDocument.Parse("""
			{
			  "type": "object",
			  "properties": {
			    "seedId": { "type": "string" }
			  },
			  "additionalProperties": false
			}
			""").RootElement.Clone(),
			returns: IntegrationTestStateResponseSchema(),
			annotations: new ExtensionToolAnnotations
			{
				Idempotent = true,
				Category = "testing",
			});

		testControl.MapTool(
			"state",
			"Returns a non-sensitive fingerprint of deterministic AppKit fixture state.",
			"GET",
			"state",
			async _ =>
			{
				var snapshot = await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(
					() => state.Snapshot(Shell.Current?.CurrentState?.Location?.ToString()));
				return HttpResponse.Json(ToIntegrationTestStateResponse(snapshot));
			},
			returns: IntegrationTestStateResponseSchema(),
			annotations: new ExtensionToolAnnotations
			{
				ReadOnly = true,
				Idempotent = true,
				Category = "testing",
			});
	}

	static JsonElement IntegrationTestStateResponseSchema()
		=> JsonDocument.Parse("""
		{
		  "type": "object",
		  "properties": {
		    "seedId": { "type": "string" },
		    "seedFingerprint": { "type": "string" },
		    "backendStateFingerprint": { "type": "string" },
		    "stateFingerprint": { "type": "string" },
		    "processInstanceId": { "type": "string" },
		    "route": { "type": ["string", "null"] }
		  }
		}
		""").RootElement.Clone();

	static object ToIntegrationTestStateResponse(IntegrationTestStateSnapshot snapshot)
		=> new
		{
			seedId = snapshot.SeedId,
			seedFingerprint = snapshot.SeedFingerprint,
			backendStateFingerprint = snapshot.BackendStateFingerprint,
			stateFingerprint = snapshot.StateFingerprint,
			processInstanceId = snapshot.ProcessInstanceId,
			route = snapshot.Route,
		};
#endif

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiAppMacOS<App>()
			.AddMacOSEssentials()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Blazor WebView
		builder.Services.AddMauiBlazorWebView();
		builder.AddMacOSBlazorWebView();

		// Shared data
		var todoService = new TodoService();
		builder.Services.AddSingleton(todoService);
#if DEBUG && DEVFLOW_INTEGRATION_TEST
		var integrationTestState = new IntegrationTestState(todoService);
		builder.Services.AddSingleton(integrationTestState);
		var launchSeed = Environment.GetEnvironmentVariable("DEVFLOW_INTEGRATION_TEST_SEED");
		if (!string.IsNullOrWhiteSpace(launchSeed))
			integrationTestState.ApplySeed(launchSeed, route: "//native");
#endif

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();

#if DEBUG
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableProfiler = true;
#if DEBUG && DEVFLOW_INTEGRATION_TEST
			ConfigureIntegrationTestExtension(options, integrationTestState);
#endif
		});
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
