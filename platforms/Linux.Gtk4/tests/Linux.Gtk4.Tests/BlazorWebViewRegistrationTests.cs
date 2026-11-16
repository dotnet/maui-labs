using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using GtkBlazorWebViewHandler = Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView.BlazorWebViewHandler;
using MauiBlazorWebView = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public sealed class BlazorWebViewRegistrationTests
{
	[Fact]
	public void AddLinuxGtk4BlazorWebView_RegistersSharedServicesAndGtkHandler()
	{
		var builder = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>();
		builder.Services.AddLinuxGtk4BlazorWebView();

		using var app = builder.Build();
		var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

		Assert.Equal(typeof(GtkBlazorWebViewHandler), handlers.GetHandlerType(typeof(MauiBlazorWebView)));
	}
}
