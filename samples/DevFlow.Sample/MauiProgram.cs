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
		builder.Services.AddSingleton<TodoService>();

		// HTTP client factory (for network monitoring demo)
		builder.Services.AddHttpClient();

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();
		builder.Services.AddTransient<NetworkTestPage>();

#if DEBUG
		//builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableProfiler = true;

			var diagnostics = options.RegisterExtension(
				"com.example.diagnostics",
				"Sample diagnostics extension",
				"1.0.0",
				new[] { "build_info", "echo", "seed_pref" });

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

			// Writes a preference directly via Microsoft.Maui.Storage.Preferences,
			// bypassing DevFlow's /preferences endpoints (so the key is NOT recorded
			// in DevFlow's tracked-key registry). Reproduces issue #344: such keys must
			// still appear in `preferences list` via native store enumeration.
			diagnostics.MapTool(
				"seed_pref",
				"Writes a preference directly via the app's Preferences store, bypassing DevFlow tracking.",
				"POST",
				"seed-pref",
				request =>
				{
					using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
					var root = document.RootElement;
					var key = root.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
					if (string.IsNullOrEmpty(key))
						return Task.FromResult(HttpResponse.Error("'key' is required."));

					var value = root.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
					var sharedName = root.TryGetProperty("sharedName", out var sharedElement) ? sharedElement.GetString() : null;

					if (string.IsNullOrEmpty(sharedName))
						Microsoft.Maui.Storage.Preferences.Default.Set(key, value ?? string.Empty);
					else
						Microsoft.Maui.Storage.Preferences.Default.Set(key, value ?? string.Empty, sharedName);

					return Task.FromResult(HttpResponse.Json(new
					{
						key,
						value = value ?? string.Empty,
						sharedName,
						seeded = true
					}));
				},
				parameters: JsonDocument.Parse("""
				{
				  "type": "object",
				  "properties": {
				    "key": { "type": "string" },
				    "value": { "type": "string" },
				    "sharedName": { "type": "string" }
				  },
				  "required": ["key"]
				}
				""").RootElement.Clone(),
				returns: JsonDocument.Parse("""
				{
				  "type": "object",
				  "properties": {
				    "key": { "type": "string" },
				    "value": { "type": "string" },
				    "sharedName": { "type": "string" },
				    "seeded": { "type": "boolean" }
				  }
				}
				""").RootElement.Clone(),
				annotations: new ExtensionToolAnnotations
				{
					Category = "diagnostics"
				});
		});
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
