#if NET11_0_OR_GREATER
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using GtkBlazorWebViewHandler = Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView.BlazorWebViewHandler;
using MauiBlazorWebView = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class ModernizationRegistrationTests
{
	[Fact]
	public void UseMauiAppLinuxGtk4_RegistersTypedGestureFactory()
	{
		using var app = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>()
			.Build();

		Assert.IsType<GtkGesturePlatformManagerFactory>(
			app.Services.GetRequiredService<IGesturePlatformManagerFactory>());
	}

	[Fact]
	public void AddLinuxGtk4BlazorWebView_UsesGtkPlatformHandler()
	{
		var builder = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>();
		builder.Services.AddLinuxGtk4BlazorWebView();

		using var app = builder.Build();
		var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

		Assert.Equal(typeof(GtkBlazorWebViewHandler), handlers.GetHandlerType(typeof(MauiBlazorWebView)));
	}
}
#endif
