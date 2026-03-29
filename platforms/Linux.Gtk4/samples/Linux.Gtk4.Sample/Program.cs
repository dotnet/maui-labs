using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Sample;

public class Program : GtkMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public static void Main(string[] args)
	{
		GtkBlazorWebView.InitializeWebKit();

		var app = new Program();
		app.Run(args);
	}
}
