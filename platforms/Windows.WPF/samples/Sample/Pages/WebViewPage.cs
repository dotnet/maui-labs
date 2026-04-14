using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class WebViewPage : ContentPage
{
	public WebViewPage()
	{
		Title = "WebView";

		var webView = new WebView
		{
			Source = "https://dotnet.microsoft.com",
			HeightRequest = 500,
		};

		var urlEntry = new Entry
		{
			Text = "https://dotnet.microsoft.com",
			Placeholder = "Enter URL...",
			HorizontalOptions = LayoutOptions.Fill,
		};

		var backBtn = new Button { Text = "← Back" };
		backBtn.Clicked += (s, e) => webView.GoBack();

		var forwardBtn = new Button { Text = "Forward →" };
		forwardBtn.Clicked += (s, e) => webView.GoForward();

		var reloadBtn = new Button { Text = "⟳ Reload" };
		reloadBtn.Clicked += (s, e) => webView.Reload();

		var goBtn = new Button { Text = "Go", BackgroundColor = Colors.DodgerBlue, TextColor = Colors.White };
		goBtn.Clicked += (s, e) =>
		{
			var url = urlEntry.Text?.Trim();
			if (!string.IsNullOrEmpty(url))
			{
				if (!url.StartsWith("http://") && !url.StartsWith("https://"))
					url = "https://" + url;
				webView.Source = url;
			}
		};

		webView.Navigated += (s, e) =>
		{
			urlEntry.Text = e.Url;
		};

		Grid.SetColumn(goBtn, 1);

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					new HorizontalStackLayout
					{
						Spacing = 8,
						Children = { backBtn, forwardBtn, reloadBtn }
					},

					new Grid
					{
						ColumnDefinitions =
						{
							new ColumnDefinition(GridLength.Star),
							new ColumnDefinition(GridLength.Auto),
						},
						ColumnSpacing = 8,
						Children =
						{
							urlEntry,
							goBtn,
						}
					},

					webView,
				}
			}
		};
	}
}
