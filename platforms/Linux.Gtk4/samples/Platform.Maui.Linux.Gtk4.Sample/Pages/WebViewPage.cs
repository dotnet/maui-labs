using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class WebViewPage : ContentPage
{
	public WebViewPage()
	{
		Title = "WebView";

		var urlEntry = new Entry
		{
			Text = "https://dotnet.microsoft.com",
			Placeholder = "Enter URL...",
		};

		var statusLabel = new Label
		{
			Text = "Ready",
			FontSize = 12,
			TextColor = Colors.Gray,
		};

		var webView = new Microsoft.Maui.Controls.WebView
		{
			HeightRequest = 500,
			Source = new UrlWebViewSource { Url = "https://dotnet.microsoft.com" },
		};

		var goButton = new Button { Text = "Go", BackgroundColor = Colors.DodgerBlue, TextColor = Colors.White };
		goButton.Clicked += (s, e) =>
		{
			var url = urlEntry.Text?.Trim();
			if (!string.IsNullOrEmpty(url))
			{
				if (!url.StartsWith("http"))
					url = "https://" + url;
				webView.Source = new UrlWebViewSource { Url = url };
				statusLabel.Text = $"Loading: {url}";
			}
		};

		var htmlButton = new Button { Text = "Load HTML Sample" };
		htmlButton.Clicked += (s, e) =>
		{
			webView.Source = new HtmlWebViewSource
			{
				Html = """
				<html>
				<body style="font-family: sans-serif; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; min-height: 90vh;">
				<h1>🐧 HTML Content in WebView</h1>
				<p>This HTML was loaded directly into the WebView using <code>HtmlWebViewSource</code>.</p>
				<p>The WebView is powered by <strong>WebKitGTK 6.0</strong> on Linux.</p>
				<button onclick="this.textContent='Clicked! JS works! 🎉'" style="padding: 12px 24px; font-size: 16px; border: none; border-radius: 8px; background: rgba(255,255,255,0.2); color: white; cursor: pointer; backdrop-filter: blur(4px);">
				    Click me (JavaScript test)
				</button>
				<hr style="border-color: rgba(255,255,255,0.3);">
				<p style="font-size: 12px; opacity: 0.7;">WebKitGTK + GirCore.WebKit-6.0 + .NET MAUI 10</p>
				</body>
				</html>
				"""
			};
			statusLabel.Text = "Loaded HTML content";
		};

		Content = new VerticalStackLayout
		{
			Spacing = 8,
			Padding = new Thickness(24),
			Children =
			{
				new Label { Text = "WebView", FontSize = 24, FontAttributes = FontAttributes.Bold },
				new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },
				new HorizontalStackLayout
				{
					Spacing = 8,
					Children = { urlEntry, goButton, htmlButton }
				},
				statusLabel,
				webView,
			}
		};
	}
}
