using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class GtkBlazorWebViewLifetimeTests
{
	[GtkRuntimeFact]
	public void Dispose_DisconnectsDetailedScriptMessageSignal_AndIsIdempotent()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		using var view = new GtkBlazorWebView(app.Services);
		var webView = Assert.IsType<WebKit.WebView>(view.Widget);
		var manager = webView.GetUserContentManager();
		var signal = GObject.Functions.SignalLookup("script-message-received", WebKit.UserContentManager.GetGType());
		var detail = GLib.Functions.QuarkFromString("mauilinuxinterop");
		Assert.NotEqual(0u, signal);
		Assert.True(GObject.Functions.SignalHasHandlerPending(manager, signal, detail, true));

		view.Dispose();
		Assert.False(GObject.Functions.SignalHasHandlerPending(manager, signal, detail, true));
		view.Dispose();
		Assert.False(GObject.Functions.SignalHasHandlerPending(manager, signal, detail, true));
		GC.KeepAlive(manager);
	}
}
