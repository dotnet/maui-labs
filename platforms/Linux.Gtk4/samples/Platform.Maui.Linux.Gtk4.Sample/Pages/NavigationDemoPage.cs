using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class NavigationDemoPage : ContentPage
{
	private readonly int _depth;

	public NavigationDemoPage(int depth = 1)
	{
		_depth = depth;
		Title = $"Nav Depth {depth}";

		var depthLabel = new Label
		{
			Text = $"You are {depth} level{(depth > 1 ? "s" : "")} deep in the navigation stack.",
			FontSize = 14,
			TextColor = Colors.Gray,
		};

		var pushButton = new Button
		{
			Text = $"Push Page (→ Depth {depth + 1})",
			BackgroundColor = Colors.DodgerBlue,
			TextColor = Colors.White,
		};
		pushButton.Clicked += async (s, e) =>
		{
			await Navigation.PushAsync(new NavigationDemoPage(depth + 1));
		};

		var popButton = new Button
		{
			Text = "Pop Page (← Go Back)",
			BackgroundColor = depth > 1 ? Colors.Coral : Colors.Gray,
			TextColor = Colors.White,
			IsEnabled = depth > 1,
		};
		popButton.Clicked += async (s, e) =>
		{
			if (depth > 1)
				await Navigation.PopAsync();
		};

		Content = new VerticalStackLayout
		{
			Spacing = 16,
			Padding = new Thickness(24),
			Children =
			{
				new Label { Text = "Navigation Demo", FontSize = 24, FontAttributes = FontAttributes.Bold },
				new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

				new Border
				{
					Stroke = DepthColor(depth),
					StrokeThickness = 2,
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

				new BoxView { HeightRequest = 1, Color = Colors.LightGray },

				new Label
				{
					Text = "Each push creates a new page instance on the navigation stack. " +
						   "The NavigationPageHandler uses Gtk.Stack with slide transitions.",
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
