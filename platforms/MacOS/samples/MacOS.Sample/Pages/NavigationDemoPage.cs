using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.Sample.Pages;

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
			Text = "Sheet Modal (Default)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalButton.Clicked += async (s, e) =>
		{
			await Navigation.PushModalAsync(CreateModalPage("Default Sheet", "Full-size native AppKit sheet.\nSlides down from the titlebar."));
		};

		var pushModalSmallButton = new Button
		{
			Text = "Sheet Modal (400×300)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalSmallButton.Clicked += async (s, e) =>
		{
			var page = CreateModalPage("Small Sheet", "Custom sized sheet (400×300).\nResizable with min constraints.");
			MacOSPage.SetModalSheetWidth(page, 400);
			MacOSPage.SetModalSheetHeight(page, 300);
			MacOSPage.SetModalSheetMinWidth(page, 300);
			MacOSPage.SetModalSheetMinHeight(page, 200);
			await Navigation.PushModalAsync(page);
		};

		var pushModalContentButton = new Button
		{
			Text = "Sheet Modal (Sizes to Content)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalContentButton.Clicked += async (s, e) =>
		{
			var page = CreateModalPage("Content-Sized Sheet", "This sheet measured its content\nand sized itself to fit.");
			MacOSPage.SetModalSheetSizesToContent(page, true);
			MacOSPage.SetModalSheetMinWidth(page, 250);
			MacOSPage.SetModalSheetMinHeight(page, 150);
			await Navigation.PushModalAsync(page);
		};

		var pushModalOverlayButton = new Button
		{
			Text = "Overlay Modal (Old Style)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalOverlayButton.Clicked += async (s, e) =>
		{
			var page = CreateModalPage("Overlay Modal", "Overlay presentation style.\nBackdrop + rounded effect view.");
			MacOSPage.SetModalPresentationStyle(page, MacOSModalPresentationStyle.Overlay);
			await Navigation.PushModalAsync(page);
		};

		var pushModalWindowButton = new Button
		{
			Text = "Window Modal",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushModalWindowButton.Clicked += async (s, e) =>
		{
			var page = CreateStackableModalPage(1, MacOSModalPresentationStyle.Window);
			await Navigation.PushModalAsync(page);
		};

		var pushStackedSheetButton = new Button
		{
			Text = "Stacked Sheets (Nested)",
			Padding = new Thickness(16, 8),
			HorizontalOptions = LayoutOptions.Start,
		};
		pushStackedSheetButton.Clicked += async (s, e) =>
		{
			var page = CreateStackableModalPage(1, MacOSModalPresentationStyle.Sheet);
			await Navigation.PushModalAsync(page);
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

				new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

				new Label { Text = "Modal Presentations", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.CornflowerBlue },
				pushModalButton,
				pushModalSmallButton,
				pushModalContentButton,
				pushModalOverlayButton,
				pushModalWindowButton,
				pushStackedSheetButton,

				new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

				new Label
				{
					Text = "• Push/Pop tests the navigation bar with back button\n• \"No NavBar\" hides the navigation bar on the pushed page\n• Sheet modals use native NSWindow.BeginSheet\n• Overlay modal uses the old backdrop + effect view style\n• Window modal uses a child NSWindow that blocks the parent\n• Stacked sheets nest sheet-on-sheet with push buttons inside each modal",
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

	ContentPage CreateModalPage(string title, string description) => new ContentPage
	{
		Title = title,
		Content = new VerticalStackLayout
		{
			Spacing = 16,
			Padding = new Thickness(32),
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Children =
			{
				new Label { Text = $"🪟 {title}", FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
				new Label { Text = description, FontSize = 14, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
				new Button
				{
					Text = "Dismiss Modal",
					Padding = new Thickness(16, 8),
					Command = new Command(async () => await Navigation.PopModalAsync()),
				}
			}
		}
	};

	ContentPage CreateStackableModalPage(int depth, MacOSModalPresentationStyle style)
	{
		var page = new ContentPage { Title = $"Modal #{depth} ({style})" };

		MacOSPage.SetModalPresentationStyle(page, style);
		MacOSPage.SetModalSheetWidth(page, Math.Max(300, 500 - (depth - 1) * 40));
		MacOSPage.SetModalSheetHeight(page, Math.Max(250, 400 - (depth - 1) * 30));
		MacOSPage.SetModalSheetMinWidth(page, 250);
		MacOSPage.SetModalSheetMinHeight(page, 200);

		var pushSheetBtn = new Button
		{
			Text = $"Push Sheet #{depth + 1}",
			Padding = new Thickness(16, 8),
		};
		pushSheetBtn.Clicked += async (s, e) =>
		{
			var next = CreateStackableModalPage(depth + 1, MacOSModalPresentationStyle.Sheet);
			await page.Navigation.PushModalAsync(next);
		};

		var pushWindowBtn = new Button
		{
			Text = $"Push Window #{depth + 1}",
			Padding = new Thickness(16, 8),
		};
		pushWindowBtn.Clicked += async (s, e) =>
		{
			var next = CreateStackableModalPage(depth + 1, MacOSModalPresentationStyle.Window);
			await page.Navigation.PushModalAsync(next);
		};

		var dismissBtn = new Button
		{
			Text = "Dismiss",
			Padding = new Thickness(16, 8),
			BackgroundColor = Colors.OrangeRed,
			TextColor = Colors.White,
		};
		dismissBtn.Clicked += async (s, e) =>
		{
			await page.Navigation.PopModalAsync();
		};

		page.Content = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(24),
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Children =
			{
				new Label
				{
					Text = $"Modal #{depth}",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = $"Style: {style}",
					FontSize = 14,
					TextColor = Colors.Gray,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				pushSheetBtn,
				pushWindowBtn,
				dismissBtn,
			}
		};

		return page;
	}
}
