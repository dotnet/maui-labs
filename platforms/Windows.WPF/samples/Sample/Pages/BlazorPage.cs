#if WPFAPP
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class BlazorPage : ContentPage
{
	public BlazorPage()
	{
		Title = "Blazor Hybrid";

		Content = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(20),
			VerticalOptions = LayoutOptions.Center,
			Children =
			{
				new Label
				{
					Text = "Blazor Hybrid",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "Blazor Hybrid WebView support available via WPF BlazorWebView",
					FontSize = 14,
					TextColor = Colors.Gray,
					HorizontalTextAlignment = TextAlignment.Center,
				},
			}
		};
	}
}
#endif


