using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class WebViewPageState
	{
		public string CurrentUrl { get; set; } = "https://dotnet.microsoft.com";
	}

	public class WebViewPage : Component<WebViewPageState>
	{
		WebView webView;

		public override View Render()
		{
			webView = new WebView();
			webView.Source = State.CurrentUrl;
			webView.OnNavigated = url => SetState(s => s.CurrentUrl = url);

			// Minimal test: WebView with explicit size + debug border
			return new VStack(spacing: 10)
			{
				HStack(8,
					Button("← Back", () => ((IWebView)webView).GoBack()),
					Button("Forward →", () => ((IWebView)webView).GoForward()),
					Button("⟳ Reload", () => ((IWebView)webView).Reload())
				),

				HStack(8,
					TextField(() => State.CurrentUrl, () => "Enter URL...")
						.OnTextChanged(v => SetState(s => s.CurrentUrl = v ?? "")),
					Button("Go", () =>
					{
						var url = State.CurrentUrl?.Trim();
						if (!string.IsNullOrEmpty(url))
						{
							if (!url.StartsWith("http://") && !url.StartsWith("https://"))
								url = "https://" + url;
							webView.Source = url;
							SetState(s => s.CurrentUrl = url);
						}
					})
					.Background(Colors.DodgerBlue)
					.Color(Colors.White)
					.Frame(width: 60)
				),

				webView
					.Frame(height: DeviceInfo.Idiom == DeviceIdiom.Phone ? 400 : 600)
			}
			.Title("WebView")
			.Padding(20);
		}
	}
}
