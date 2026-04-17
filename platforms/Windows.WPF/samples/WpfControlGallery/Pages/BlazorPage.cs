using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public class BlazorPage : ContentPage
{
	public BlazorPage()
	{
		// The WPF Blazor hybrid path depends on Microsoft.AspNetCore.Components.WebView.Wpf which
		// internally hosts a WebView2CompositionControl. Its WPF ControlTemplate references types
		// from Microsoft.Windows.SDK.NET (the WinRT projection assembly). When the host project
		// targets plain `net10.0-windows` (no Windows SDK version), that assembly is unresolvable
		// and WPF throws FileNotFoundException from ApplyTemplate during the measure pass — too
		// late to catch inside a handler. Probe at construction by attempting to load the assembly
		// by full identity; if it isn't resolvable, render a fallback explaining the limitation.
		var sdkAvailable = false;
		string? probeError = null;
		try
		{
			System.Reflection.Assembly.Load(
				"Microsoft.Windows.SDK.NET, Version=10.0.17763.10, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
			sdkAvailable = true;
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

