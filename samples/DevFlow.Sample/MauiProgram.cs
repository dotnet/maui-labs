using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Blazor;

namespace DevFlow.Sample;

public static class MauiProgram
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
			"Deterministic DevFlow.Sample lifecycle support for the integration-test build only.",
			"1.0.0",
			new[] { "seed", "state-fingerprint" });

		testControl.MapTool(
			"seed",
			"Resets the in-memory sample data to the deterministic integration-test seed.",
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
			returns: JsonDocument.Parse("""
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
			""").RootElement.Clone(),
			annotations: new ExtensionToolAnnotations
			{
				Idempotent = true,
				Category = "testing"
			});

		testControl.MapTool(
			"state",
			"Returns a non-sensitive fingerprint of the deterministic sample state.",
			"GET",
			"state",
			async _ =>
			{
				var snapshot = await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(
					() => state.Snapshot(Shell.Current?.CurrentState?.Location?.ToString()));
				return HttpResponse.Json(ToIntegrationTestStateResponse(snapshot));
			},
			returns: JsonDocument.Parse("""
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
			""").RootElement.Clone(),
			annotations: new ExtensionToolAnnotations
			{
				ReadOnly = true,
				Idempotent = true,
				Category = "testing"
			});
	}

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
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Blazor WebView
		builder.Services.AddMauiBlazorWebView();

		// Shared data
		var todoService = new TodoService();
		builder.Services.AddSingleton(todoService);
#if DEBUG && DEVFLOW_INTEGRATION_TEST
		var integrationTestState = new IntegrationTestState(todoService);
		builder.Services.AddSingleton(integrationTestState);
		// Apple XCTest starts a fresh target process for each clean QA attempt. The seed is
		// accepted only in the explicit integration-test build and is applied before any page
		// resolves the singleton service, so Mac Catalyst reset never touches user storage.
		var launchSeed = Environment.GetEnvironmentVariable("DEVFLOW_INTEGRATION_TEST_SEED");
		if (!string.IsNullOrWhiteSpace(launchSeed))
			integrationTestState.ApplySeed(launchSeed, route: "//native");
#endif

		// HTTP client factory (for network monitoring demo)
		builder.Services.AddHttpClient();

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();
		builder.Services.AddTransient<NetworkTestPage>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableProfiler = true;
#if DEBUG && DEVFLOW_INTEGRATION_TEST
			ConfigureIntegrationTestExtension(options, integrationTestState);
#endif

			var diagnostics = options.RegisterExtension(
				"com.example.diagnostics",
				"Sample diagnostics extension",
				"1.0.0",
				new[] { "build_info", "echo" });

			diagnostics.MapTool(
				"build_info",
				"Returns sample app build information.",
				"GET",
				"build-info",
				_ => Task.FromResult(HttpResponse.Json(new
				{
					app = AppInfo.Current.Name,
					version = AppInfo.Current.VersionString,
					build = AppInfo.Current.BuildString
				})),
				returns: JsonDocument.Parse("""
				{
				  "type": "object",
				  "properties": {
				    "app": { "type": "string" },
				    "version": { "type": "string" },
				    "build": { "type": "string" }
				  }
				}
				""").RootElement.Clone(),
				annotations: new ExtensionToolAnnotations
				{
					ReadOnly = true,
					Idempotent = true,
					Category = "diagnostics"
				});

			diagnostics.MapTool(
				"echo",
				"Echoes a JSON request body back to the caller.",
				"POST",
				"echo",
				request =>
				{
					using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
					return Task.FromResult(HttpResponse.Json(new
					{
						body = document.RootElement.Clone()
					}));
				},
				parameters: JsonDocument.Parse("""
				{
				  "type": "object",
				  "additionalProperties": true
				}
				""").RootElement.Clone(),
				returns: JsonDocument.Parse("""
				{
				  "type": "object",
				  "properties": {
				    "body": { "type": "object" }
				  }
				}
				""").RootElement.Clone(),
				annotations: new ExtensionToolAnnotations
				{
					Idempotent = true,
					Category = "diagnostics"
				});
		});
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
