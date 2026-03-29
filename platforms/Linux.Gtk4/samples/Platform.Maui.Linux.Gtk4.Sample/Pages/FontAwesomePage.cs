using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MauiIcons.FontAwesome.Solid;
using MauiIcons.Core;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class FontAwesomePage : ContentPage
{
	public FontAwesomePage()
	{
		Title = "FontAwesome Icons";
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
						Text = "🎨 FontAwesome Icons",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					},
					new Label
					{
						Text = "Using AathifMahir.Maui.MauiIcons.FontAwesome (v5.0.0)",
						FontSize = 13,
						TextColor = Colors.Gray,
					},
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					// --- Section: MauiIcon control ---
					BuildSection("MauiIcon Control",
						"The built-in MauiIcon control renders FontAwesome glyphs as images.",
						BuildMauiIconGrid()),

					// --- Section: Image with FontAwesome ---
					BuildSection("Image with FontAwesome",
						"FontAwesome icons applied to Image controls via C# markup extensions.",
						BuildImageIconRow()),

					// --- Section: Button with FontAwesome ---
					BuildSection("Buttons with Icons",
						"FontAwesome icons on Button and ImageButton controls.",
						BuildButtonRow()),

					// --- Section: Icon Sizes ---
					BuildSection("Icon Sizes",
						"Same icon at different sizes: 16, 24, 32, 48, 64.",
						BuildSizeRow()),

					// --- Section: Icon Colors ---
					BuildSection("Icon Colors",
						"Same icon in different colors.",
						BuildColorRow()),

					// --- Section: Labels with Icons ---
					BuildSection("Labels with Icons",
						"FontAwesome icons rendered inline as label text (glyph font).",
						BuildLabelIconRow()),
				}
			}
		};
	}

	static View BuildSection(string title, string description, View content)
	{
		return new Border
		{
			Stroke = Colors.LightGray,
			StrokeThickness = 1,
			Padding = new Thickness(16),
			Content = new VerticalStackLayout
			{
				Spacing = 8,
				Children =
				{
					new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold },
					new Label { Text = description, FontSize = 12, TextColor = Colors.Gray },
					content,
				}
			}
		};
	}

	static View BuildMauiIconGrid()
	{
		var icons = new (FontAwesomeSolidIcons icon, string name, Color color)[]
		{
			(FontAwesomeSolidIcons.Star, "Star", Colors.Gold),
			(FontAwesomeSolidIcons.Heart, "Heart", Colors.Red),
			(FontAwesomeSolidIcons.Bell, "Bell", Colors.Orange),
			(FontAwesomeSolidIcons.Bookmark, "Bookmark", Colors.DodgerBlue),
			(FontAwesomeSolidIcons.Envelope, "Envelope", Colors.Teal),
			(FontAwesomeSolidIcons.Calendar, "Calendar", Colors.Purple),
			(FontAwesomeSolidIcons.Comment, "Comment", Colors.Green),
			(FontAwesomeSolidIcons.Circle, "Circle", Colors.Crimson),
			(FontAwesomeSolidIcons.Clock, "Clock", Colors.SteelBlue),
			(FontAwesomeSolidIcons.Flag, "Flag", Colors.OrangeRed),
			(FontAwesomeSolidIcons.Folder, "Folder", Colors.Goldenrod),
			(FontAwesomeSolidIcons.Eye, "Eye", Colors.MediumPurple),
		};

		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star),
			},
			RowSpacing = 12,
			ColumnSpacing = 12,
		};

		for (int i = 0; i < icons.Length; i++)
		{
			var (icon, name, color) = icons[i];
			int row = i / 4;
			int col = i % 4;

			if (grid.RowDefinitions.Count <= row)
				grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

			var cell = new VerticalStackLayout
			{
				Spacing = 4,
				HorizontalOptions = LayoutOptions.Center,
				Children =
				{
					new MauiIcon
					{
						Icon = icon,
						IconColor = color,
						IconSize = 32,
					},
					new Label
					{
						Text = name,
						FontSize = 10,
						HorizontalTextAlignment = TextAlignment.Center,
						TextColor = Colors.Gray,
					},
				}
			};

			grid.Add(cell, col, row);
		}

		return grid;
	}

	static View BuildImageIconRow()
	{
		var icons = new (FontAwesomeSolidIcons icon, Color color)[]
		{
			(FontAwesomeSolidIcons.Star, Colors.Gold),
			(FontAwesomeSolidIcons.Heart, Colors.Red),
			(FontAwesomeSolidIcons.ThumbsUp, Colors.DodgerBlue),
			(FontAwesomeSolidIcons.Snowflake, Colors.LightBlue),
			(FontAwesomeSolidIcons.Moon, Colors.Indigo),
			(FontAwesomeSolidIcons.Sun, Colors.Orange),
		};

		var layout = new HorizontalStackLayout { Spacing = 16 };
		foreach (var (icon, color) in icons)
			layout.Children.Add(new MauiIcon { Icon = icon, IconColor = color, IconSize = 36 });
		return layout;
	}

	static View BuildButtonRow()
	{
		var statusLabel = new Label { Text = "Tap a button…", FontSize = 12, TextColor = Colors.Gray };

		var btn1 = CreateIconButton("Like", FontAwesomeSolidIcons.ThumbsUp, Colors.DodgerBlue);
		btn1.Clicked += (s, e) => statusLabel.Text = "👍 Liked!";

		var btn2 = CreateIconButton("Save", FontAwesomeSolidIcons.FloppyDisk, Colors.Green);
		btn2.Clicked += (s, e) => statusLabel.Text = "💾 Saved!";

		var btn3 = CreateIconButton("Delete", FontAwesomeSolidIcons.TrashCan, Colors.Crimson);
		btn3.Clicked += (s, e) => statusLabel.Text = "🗑️ Deleted!";

		var sendBtn = new Button { Text = "Send", BackgroundColor = Colors.Purple, TextColor = Colors.White };
		sendBtn.Clicked += (s, e) => statusLabel.Text = "✈️ Sent!";

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new HorizontalStackLayout { Spacing = 12, Children = { btn1, btn2, btn3, sendBtn } },
				statusLabel,
			}
		};
	}

	static Button CreateIconButton(string text, FontAwesomeSolidIcons icon, Color bg)
	{
		return new Button
		{
			Text = text,
			BackgroundColor = bg,
			TextColor = Colors.White,
		};
	}

	static View BuildSizeRow()
	{
		var sizes = new[] { 16.0, 24.0, 32.0, 48.0, 64.0 };
		var layout = new HorizontalStackLayout { Spacing = 16 };

		foreach (var sz in sizes)
		{
			var stack = new VerticalStackLayout
			{
				Spacing = 4,
				HorizontalOptions = LayoutOptions.Center,
				Children =
				{
					new MauiIcon { Icon = FontAwesomeSolidIcons.Star, IconColor = Colors.Gold, IconSize = sz },
					new Label { Text = $"{sz}px", FontSize = 10, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
				}
			};
			layout.Children.Add(stack);
		}

		return layout;
	}

	static View BuildColorRow()
	{
		var colors = new[] { Colors.Red, Colors.Orange, Colors.Gold, Colors.Green, Colors.DodgerBlue, Colors.Purple, Colors.HotPink };
		var layout = new HorizontalStackLayout { Spacing = 12 };

		foreach (var c in colors)
		{
			layout.Children.Add(new MauiIcon { Icon = FontAwesomeSolidIcons.Heart, IconColor = c, IconSize = 32 });
		}

		return layout;
	}

	static View BuildLabelIconRow()
	{
		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				CreateIconLabel(FontAwesomeSolidIcons.Envelope, "You have new messages", Colors.Teal),
				CreateIconLabel(FontAwesomeSolidIcons.Bell, "Notifications enabled", Colors.Orange),
				CreateIconLabel(FontAwesomeSolidIcons.CircleCheck, "Verification complete", Colors.Green),
				CreateIconLabel(FontAwesomeSolidIcons.Clock, "Last updated 2 hours ago", Colors.SteelBlue),
			}
		};
	}

	static View CreateIconLabel(FontAwesomeSolidIcons icon, string text, Color iconColor)
	{
		return new HorizontalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new MauiIcon { Icon = icon, IconColor = iconColor, IconSize = 20 },
				new Label { Text = text, FontSize = 14, VerticalTextAlignment = TextAlignment.Center },
			}
		};
	}
}
