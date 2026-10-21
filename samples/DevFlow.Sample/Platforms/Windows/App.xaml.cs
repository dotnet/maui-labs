using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DevFlow.Sample.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
#if DEBUG
		UnhandledException += (_, args) => WriteUnhandledDiagnostic(args.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			WriteUnhandledDiagnostic(args.ExceptionObject as Exception);
#endif
		this.InitializeComponent();

#if DEBUG
		EnableWebViewDebugging();
#endif
	}

#if DEBUG
	static void WriteUnhandledDiagnostic(Exception? exception)
	{
		var message = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} unhandled: {exception}";
		if (exception is not null)
		{
			foreach (var frame in new System.Diagnostics.StackTrace(exception, false).GetFrames())
			{
				var method = frame.GetMethod();
				message += $"{Environment.NewLine}frame: {method?.DeclaringType?.FullName}.{method?.Name}; " +
					$"assembly={method?.Module.Assembly.FullName}; mvid={method?.Module.ModuleVersionId}; " +
					$"ilOffset={frame.GetILOffset()}; nativeOffset={frame.GetNativeOffset()}";
			}
		}
		WriteRuntimeDiagnostic(message);
	}

	internal static void WriteRuntimeDiagnostic(string message)
	{
		Console.Error.WriteLine(message);
		var path = Environment.GetEnvironmentVariable("DEVFLOW_TEST_DIAGNOSTICS_PATH");
		if (!string.IsNullOrEmpty(path))
		{
			try { File.AppendAllText(path, message + Environment.NewLine); }
			catch (IOException ex) { Console.Error.WriteLine($"Unable to write sample diagnostics: {ex}"); }
			catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"Unable to write sample diagnostics: {ex}"); }
		}
	}

	static bool IsEnabled(string? value)
		=> value is not null &&
		   (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
		    value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
		    value.Equals("yes", StringComparison.OrdinalIgnoreCase));

	static bool ShouldOpenDevToolsWindow()
		=> IsEnabled(Environment.GetEnvironmentVariable("DEVFLOW_OPEN_WEBVIEW_DEVTOOLS"));

	static void EnableWebViewDebugging()
	{
		var openDevTools = ShouldOpenDevToolsWindow();

		BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("WebViewDebugging", (handler, view) =>
		{
			if (handler.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webView2)
				return;

			static void ConfigureDevTools(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2, bool openDevTools)
			{
				coreWebView2.Settings.AreDevToolsEnabled = true;
				if (openDevTools)
					coreWebView2.OpenDevToolsWindow();
			}

			if (webView2.CoreWebView2 is { } coreWebView2)
			{
				ConfigureDevTools(coreWebView2, openDevTools);
				return;
			}

			webView2.CoreWebView2Initialized += (_, args) =>
			{
				if (args.Exception is not null || webView2.CoreWebView2 is null)
					return;

				ConfigureDevTools(webView2.CoreWebView2, openDevTools);
			};
		});
	}
#endif

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

