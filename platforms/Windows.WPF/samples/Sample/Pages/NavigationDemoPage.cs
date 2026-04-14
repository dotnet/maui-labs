using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class NavigationDemoPage : ContentPage
{
	private readonly int _depth;

	public NavigationDemoPage(int depth = 1)
	{
		_depth = depth;
		Title = $"Nav Depth {depth}";

		// Add toolbar items
		ToolbarItems.Add(new ToolbarItem("Info", null, () =>
		{
			DisplayAlert("Info", $"You are at depth {depth}", "OK");
		}));

		if (depth > 1)
		{
			ToolbarItems.Add(new ToolbarItem("Root", null, async () =>
			{
				await Navigation.PopToRootAsync();
			}));
		}

		var depthLabel = new Label
		{
			Text = $"You are {depth} level{(depth > 1 ? "s" : "")} deep in the navigation stack.",
			FontSize = 14,
			TextColor = Colors.Gray,
		};

		var pushButton = new Button
		{
			Text = $"Push Page (→ Depth {depth + 1})",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushButton.Clicked += async (s, e) =>
		{
			await Navigation.PushAsync(new NavigationDemoPage(depth + 1));
		};

		var popButton = new Button
		{
			Text = "Pop Page (← Go Back)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
			IsEnabled = depth > 1,
		};
		popButton.Clicked += async (s, e) =>
		{
			if (depth > 1)
				await Navigation.PopAsync();
		};

		var pushModalButton = new Button
		{
			Text = "Push Modal Page",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalButton.Clicked += async (s, e) =>
		{
			var modalPage = new ContentPage
			{
				Title = "Modal Page",
				Content = new VerticalStackLayout
				{
					Spacing = 16,
					Padding = new Thickness(32),
					VerticalOptions = LayoutOptions.Center,
					HorizontalOptions = LayoutOptions.Center,
					Children =
					{
						new Label { Text = "🪟 Modal Page", FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
						new Label { Text = "This page was presented modally.\nIt overlays the main content.", FontSize = 14, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
						new Button
						{
							Text = "Dismiss Modal",
							Padding = new Thickness(16, 8),
							Command = new Command(async () => await Navigation.PopModalAsync()),
						}
					}
				}
			};
			await Navigation.PushModalAsync(modalPage);
		};

		var pushNoNavBarButton = new Button
		{
			Text = "Push Page (No NavBar)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushNoNavBarButton.Clicked += async (s, e) =>
		{
			var page = new NavigationDemoPage(depth + 1);
			NavigationPage.SetHasNavigationBar(page, false);
			await Navigation.PushAsync(page);
		};

		Content = new VerticalStackLayout
		{
			Spacing = 16,
			Padding = new Thickness(24),
			Children =
			{

				new Border
				{
					Stroke = DepthColor(depth),
					StrokeThickness = 2,
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
					Padding = new Thickness(16),
					Content = new VerticalStackLayout
					{
						Spacing = 8,
						Children =
						{
							new Label
							{
								Text = $"📍 Page at Depth {depth}",
								FontSize = 20,
								FontAttributes = FontAttributes.Bold,
								TextColor = DepthColor(depth),
							},
							depthLabel,
							new Label
							{
								Text = $"Page created at: {DateTime.Now:HH:mm:ss.fff}",
								FontSize = 12,
								TextColor = Colors.Gray,
							},
						}
					}
				},

				pushButton,
				popButton,
				pushNoNavBarButton,
				pushModalButton,

				new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

				new Label
				{
					Text = "• Push/Pop tests the navigation bar with back button\n• \"No NavBar\" hides the navigation bar on the pushed page\n• \"Push Modal\" shows a modal overlay page\n• Toolbar items appear in the macOS toolbar",
					FontSize = 12,
					TextColor = Colors.Gray,
				},
			}
		};
	}

	static Color DepthColor(int depth) => depth switch
	{
		1 => Colors.DodgerBlue,
		2 => Colors.MediumSeaGreen,
		3 => Colors.Orange,
		4 => Colors.MediumPurple,
		5 => Colors.Crimson,
		_ => Colors.Teal,
	};
}
