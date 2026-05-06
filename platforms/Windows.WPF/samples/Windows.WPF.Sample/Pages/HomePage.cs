using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public class HomePage : ContentPage
{
	public HomePage()
	{
		Title = "Home";
		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					new Label
					{
						Text = "🪟 .NET MAUI on WPF",
						FontSize = 32,
						HorizontalTextAlignment = TextAlignment.Center,
						FontAttributes = FontAttributes.Bold,
					},
					new Label
					{
						Text = "Rendered natively with Windows Presentation Foundation",
						FontSize = 16,
						HorizontalTextAlignment = TextAlignment.Center,
						TextColor = Colors.Gray,
					},
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					new Label
					{
						Text = "This sample app demonstrates the Microsoft.Maui.Platforms.Windows.WPF backend — " +
							"a standalone .NET MAUI backend for Windows that maps MAUI controls " +
							"to native WPF widgets. No MAUI fork required!",
						FontSize = 14,
					},

					new Border
					{
						Stroke = Colors.DodgerBlue,
						StrokeThickness = 1,
						Padding = new Thickness(16),
						Content = new VerticalStackLayout
						{
							Spacing = 8,
							Children =
							{
								new Label { Text = "Platform Details", FontSize = 18, FontAttributes = FontAttributes.Bold },
								new Label { Text = "• 33 MAUI control handlers implemented", FontSize = 14 },
								new Label { Text = "• Native WPF controls and rendering", FontSize = 14 },
								new Label { Text = "• WebView2 for BlazorWebView", FontSize = 14 },
								new Label { Text = "• Microsoft.Maui.Graphics.Win2D-backed ICanvas for GraphicsView", FontSize = 14 },
								new Label { Text = "• .NET 10 / MAUI 10", FontSize = 14 },
								new Label { Text = $"• Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}", FontSize = 14, TextColor = Colors.Gray },
								new Label { Text = $"• OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}", FontSize = 14, TextColor = Colors.Gray },
							}
						}
					},

					new Label
					{
						Text = "Use the menu on the left to explore different control demos.",
						FontSize = 14,
						TextColor = Colors.Gray,
						HorizontalTextAlignment = TextAlignment.Center,
					},
				}
			}
		};
	}
}
