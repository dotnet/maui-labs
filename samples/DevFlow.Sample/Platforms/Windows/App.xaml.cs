using Microsoft.UI.Xaml;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;

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
		this.InitializeComponent();

#if DEBUG
		EnableWebView2Debugging();
#endif
	}

	void EnableWebView2Debugging()
	{
		BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("WebViewDebugging", async (handler, view) =>
		{
			if (handler.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2)
			{
				// Configure environment with remote debugging port for DevFlow CDP bridge
				var options = new CoreWebView2EnvironmentOptions("--remote-debugging-port=9222");
				var env = await CoreWebView2Environment.CreateAsync(null, null, options);

				await webView2.EnsureCoreWebView2Async(env);
			}
		});
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

