using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using MauiBlazorWebView = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class BlazorPage : ContentPage
{
	public BlazorPage()
	{
		var blazorWebView = new MauiBlazorWebView
		{
			HostPage = "wwwroot/index.html",
			HeightRequest = 500,
		};
		blazorWebView.RootComponents.Add(
			new RootComponent
			{
				Selector = "#app",
				ComponentType = typeof(BlazorComponents.Index),
			});

		Content = new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					Text = "Blazor Hybrid on Linux (WebKitGTK)",
					FontSize = 18,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				blazorWebView,
			}
		};
	}
}
