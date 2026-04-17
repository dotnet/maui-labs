using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public class BlazorPage : ContentPage
{
	public BlazorPage()
	{
		// The WPF Blazor hybrid path depends on Microsoft.AspNetCore.Components.WebView.Wpf which
		// pulls in WebView2CompositionControl + Microsoft.Windows.SDK.NET. If those aren't present
		// at runtime, WPF's measure pipeline throws a FileNotFoundException from ApplyTemplate
		// (which is too late to catch inside a handler).
		// Probe at construction time: only instantiate the WebView if the SDK type resolves.
		var sdkAvailable = false;
		string? probeError = null;
		try
		{
			// Touching this type forces Microsoft.Windows.SDK.NET to load.
			_ = typeof(Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView).Assembly;
			var asm = System.Reflection.Assembly.Load("Microsoft.Windows.SDK.NET");
			sdkAvailable = asm is not null;
		}
		catch (System.Exception ex)
		{
			probeError = ex.Message;
		}

		if (!sdkAvailable)
		{
			Content = BuildFallback(probeError ?? "Microsoft.Windows.SDK.NET not resolvable.");
			return;
		}

		try
		{
			var blazorWebView = new WPFBlazorWebView
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
						Text = "Blazor Hybrid (WPF)",
						FontSize = 18,
						FontAttributes = FontAttributes.Bold,
						HorizontalTextAlignment = TextAlignment.Center,
					},
					blazorWebView,
				}
			};
		}
		catch (System.Exception ex)
		{
			Content = BuildFallback(ex.Message);
		}
	}

	static View BuildFallback(string reason) => new VerticalStackLayout
	{
		Spacing = 8,
		Padding = 16,
		Children =
		{
			new Label
			{
				Text = "Blazor Hybrid (WPF)",
				FontSize = 18,
				FontAttributes = FontAttributes.Bold,
			},
			new Label
			{
				Text = "BlazorWebView is unavailable in this environment. The WPF Blazor hybrid " +
					   "control requires the WebView2 runtime and Microsoft.Windows.SDK.NET.",
				TextColor = Colors.DarkSlateGray,
			},
			new Label
			{
				Text = reason,
				FontSize = 11,
				TextColor = Colors.Gray,
			},
		}
	};
}

