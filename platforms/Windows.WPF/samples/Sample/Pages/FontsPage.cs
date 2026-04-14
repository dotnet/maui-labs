using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class FontsPage : ContentPage
{
	public FontsPage()
	{
		Title = "Fonts";

		var stack = new VerticalStackLayout { Spacing = 15, Padding = 20 };

		// System font
		stack.Children.Add(new Label
		{
			Text = "System Font (Default)",
			FontSize = 16,
		});

		// Embedded font by alias
		stack.Children.Add(new Label
		{
			Text = "OpenSans Regular (Embedded Font via alias)",
			FontFamily = "OpenSansRegular",
			FontSize = 16,
		});

		// Embedded font by filename
		stack.Children.Add(new Label
		{
			Text = "OpenSans Regular (Embedded Font via filename)",
			FontFamily = "OpenSans-Regular",
			FontSize = 16,
		});

		// Bold system font
		stack.Children.Add(new Label
		{
			Text = "System Font Bold",
			FontSize = 16,
			FontAttributes = FontAttributes.Bold,
		});

		// Italic system font
		stack.Children.Add(new Label
		{
			Text = "System Font Italic",
			FontSize = 16,
			FontAttributes = FontAttributes.Italic,
		});

		// Various sizes with embedded font
		stack.Children.Add(new Label
		{
			Text = "OpenSans Size 10",
			FontFamily = "OpenSansRegular",
			FontSize = 10,
		});
		stack.Children.Add(new Label
		{
			Text = "OpenSans Size 18",
			FontFamily = "OpenSansRegular",
			FontSize = 18,
		});
		stack.Children.Add(new Label
		{
			Text = "OpenSans Size 24",
			FontFamily = "OpenSansRegular",
			FontSize = 24,
		});

		// Named font by macOS system name
		stack.Children.Add(new Label
		{
			Text = "Menlo (macOS system font)",
			FontFamily = "Menlo",
			FontSize = 14,
		});

		stack.Children.Add(new Label
		{
			Text = "Georgia (macOS system font)",
			FontFamily = "Georgia",
			FontSize = 14,
		});

		// Button with embedded font
		stack.Children.Add(new Button
		{
			Text = "Button with OpenSans",
			FontFamily = "OpenSansRegular",
			FontSize = 14,
		});

		// Entry with embedded font
		stack.Children.Add(new Entry
		{
			Placeholder = "Entry with OpenSans",
			FontFamily = "OpenSansRegular",
			FontSize = 14,
		});

		// --- FontImageSource section ---
		stack.Children.Add(new Label
		{
			Text = "FontImageSource (Font Icons)",
			FontSize = 18,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.CornflowerBlue,
			Margin = new Thickness(0, 16, 0, 0),
		});

		stack.Children.Add(new Label { Text = "Unicode glyphs (system font):", FontSize = 13 });
		stack.Children.Add(new HorizontalStackLayout
		{
			Spacing = 12,
			Children =
			{
				FontIcon("★", Colors.Gold),
				FontIcon("♥", Colors.Red),
				FontIcon("⚡", Colors.Orange),
				FontIcon("✓", Colors.Green),
				FontIcon("⚙", Colors.Gray),
				FontIcon("✈", Colors.DodgerBlue),
				FontIcon("⌘", Colors.Purple),
				FontIcon("♻", Colors.Teal),
			}
		});

		// CupertinoIcons (MauiIcons.Cupertino) — uses the embedded Cupertino_Icons.ttf
		stack.Children.Add(new Label
		{
			Text = "Cupertino Icons (MauiIcons.Cupertino):",
			FontSize = 13,
			Margin = new Thickness(0, 8, 0, 0),
		});
		stack.Children.Add(new HorizontalStackLayout
		{
			Spacing = 12,
			Children =
			{
				// Airplane=\ue900, Alarm=\ue901, Ant=\ue904, App=\ue909, Heart=\ue9a4
				CupertinoIcon("\ue900", Colors.DodgerBlue, "Airplane"),
				CupertinoIcon("\ue901", Colors.Orange, "Alarm"),
				CupertinoIcon("\ue904", Colors.Brown, "Ant"),
				CupertinoIcon("\ue909", Colors.Teal, "App"),
				CupertinoIcon("\ue947", Colors.MediumPurple, "Bolt"),
			}
		});
		stack.Children.Add(new HorizontalStackLayout
		{
			Spacing = 12,
			Children =
			{
				// Star=\ue9fc, StarFill=\ue9fd, Gear=\ue990, GearAlt=\ue991
				CupertinoIcon("\ue9fc", Colors.Gold, "Star"),
				CupertinoIcon("\ue9fd", Colors.Gold, "StarFill"),
				CupertinoIcon("\ue990", Colors.Gray, "Gear"),
				CupertinoIcon("\ue94e", Colors.Green, "Book"),
				CupertinoIcon("\ue950", Colors.Red, "Bookmark"),
			}
		});

		// FontImageSource on buttons
		stack.Children.Add(new Label
		{
			Text = "Buttons with font icons:",
			FontSize = 13,
			Margin = new Thickness(0, 8, 0, 0),
		});
		stack.Children.Add(new HorizontalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Button
				{
					Text = " Download",
					ImageSource = new FontImageSource { Glyph = "⬇", Color = Colors.White, Size = 16 },
				},
				new Button
				{
					Text = " Favorite",
					ImageSource = new FontImageSource { Glyph = "★", Color = Colors.Gold, Size = 16 },
				},
				new Button
				{
					Text = " Settings",
					ImageSource = new FontImageSource { Glyph = "⚙", Color = Colors.White, Size = 16 },
				},
			}
		});

		// Different sizes
		stack.Children.Add(new Label
		{
			Text = "Various sizes:",
			FontSize = 13,
			Margin = new Thickness(0, 8, 0, 0),
		});
		stack.Children.Add(new HorizontalStackLayout
		{
			Spacing = 16,
			VerticalOptions = LayoutOptions.End,
			Children =
			{
				FontIcon("★", Colors.Gold, 16),
				FontIcon("★", Colors.Gold, 24),
				FontIcon("★", Colors.Gold, 32),
				FontIcon("★", Colors.Gold, 48),
				FontIcon("★", Colors.Gold, 64),
			}
		});

		Content = new ScrollView { Content = stack };
	}

	static Image FontIcon(string glyph, Color color, double size = 32) => new()
	{
		Source = new FontImageSource { Glyph = glyph, Color = color, Size = size },
		WidthRequest = size,
		HeightRequest = size,
	};

	static VerticalStackLayout CupertinoIcon(string glyph, Color color, string label) => new()
	{
		Spacing = 2,
		Children =
		{
			new Image
			{
				Source = new FontImageSource { Glyph = glyph, FontFamily = "CupertinoIcons", Color = color, Size = 28 },
				WidthRequest = 28,
				HeightRequest = 28,
				HorizontalOptions = LayoutOptions.Center,
			},
			new Label { Text = label, FontSize = 10, HorizontalTextAlignment = TextAlignment.Center },
		}
	};
}
