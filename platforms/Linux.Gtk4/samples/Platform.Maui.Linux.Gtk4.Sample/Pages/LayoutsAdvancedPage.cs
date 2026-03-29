using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class LayoutsAdvancedPage : ContentPage
{
	public LayoutsAdvancedPage()
	{
		Title = "Advanced Layouts";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(16),
				Children =
				{
					new Label { Text = "Advanced Layouts", FontSize = 22, FontAttributes = FontAttributes.Bold },

					// --- FlexLayout ---
					new Label { Text = "FlexLayout (Wrap)", FontSize = 16, FontAttributes = FontAttributes.Bold },
					BuildFlexLayoutDemo(),

					new Label { Text = "FlexLayout (JustifyContent)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					BuildFlexJustifyDemo(),

					// --- AbsoluteLayout ---
					new Label { Text = "AbsoluteLayout", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					BuildAbsoluteLayoutDemo(),

					new Label { Text = "AbsoluteLayout (Proportional)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					BuildAbsoluteProportionalDemo(),
				}
			}
		};
	}

	static FlexLayout BuildFlexLayoutDemo()
	{
		var flex = new FlexLayout
		{
			Wrap = FlexWrap.Wrap,
			JustifyContent = FlexJustify.Start,
			AlignItems = FlexAlignItems.Center,
			BackgroundColor = Color.FromRgba(0, 0, 0, 0.05),
			Padding = new Thickness(8),
		};

		var colors = new[] { Colors.Coral, Colors.CornflowerBlue, Colors.MediumSeaGreen,
			Colors.Orchid, Colors.Gold, Colors.Tomato, Colors.SteelBlue, Colors.Peru };

		for (int i = 0; i < colors.Length; i++)
		{
			var box = new BoxView
			{
				Color = colors[i],
				WidthRequest = 60 + (i % 3) * 20,
				HeightRequest = 40,
				Margin = new Thickness(4),
			};
			flex.Children.Add(box);
		}

		return flex;
	}

	static FlexLayout BuildFlexJustifyDemo()
	{
		var flex = new FlexLayout
		{
			Direction = FlexDirection.Row,
			JustifyContent = FlexJustify.SpaceEvenly,
			AlignItems = FlexAlignItems.Center,
			HeightRequest = 60,
			BackgroundColor = Color.FromRgba(0, 0, 0, 0.05),
			Padding = new Thickness(8),
		};

		flex.Children.Add(new Label { Text = "A", FontSize = 18, TextColor = Colors.White, BackgroundColor = Colors.CornflowerBlue, Padding = new Thickness(12, 6) });
		flex.Children.Add(new Label { Text = "B", FontSize = 18, TextColor = Colors.White, BackgroundColor = Colors.Coral, Padding = new Thickness(12, 6) });
		flex.Children.Add(new Label { Text = "C", FontSize = 18, TextColor = Colors.White, BackgroundColor = Colors.MediumSeaGreen, Padding = new Thickness(12, 6) });

		return flex;
	}

	static AbsoluteLayout BuildAbsoluteLayoutDemo()
	{
		var layout = new AbsoluteLayout
		{
			HeightRequest = 160,
			BackgroundColor = Color.FromRgba(0, 0, 0, 0.05),
		};

		var red = new BoxView { Color = Colors.Tomato };
		AbsoluteLayout.SetLayoutBounds(red, new Rect(10, 10, 100, 60));
		layout.Children.Add(red);

		var blue = new BoxView { Color = Colors.CornflowerBlue };
		AbsoluteLayout.SetLayoutBounds(blue, new Rect(80, 40, 120, 80));
		layout.Children.Add(blue);

		var green = new BoxView { Color = Colors.MediumSeaGreen };
		AbsoluteLayout.SetLayoutBounds(green, new Rect(170, 20, 80, 100));
		layout.Children.Add(green);

		var label = new Label { Text = "Overlapping boxes", TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
		AbsoluteLayout.SetLayoutBounds(label, new Rect(15, 130, 200, 25));
		layout.Children.Add(label);

		return layout;
	}

	static AbsoluteLayout BuildAbsoluteProportionalDemo()
	{
		var layout = new AbsoluteLayout
		{
			HeightRequest = 120,
			BackgroundColor = Color.FromRgba(0, 0, 0, 0.05),
		};

		// Proportional: fill 50% width, anchored to left
		var left = new BoxView { Color = Colors.Orchid };
		AbsoluteLayout.SetLayoutBounds(left, new Rect(0, 0, 0.5, 1));
		AbsoluteLayout.SetLayoutFlags(left, AbsoluteLayoutFlags.All);
		layout.Children.Add(left);

		// Proportional: fill 50% width, anchored to right
		var right = new BoxView { Color = Colors.Gold };
		AbsoluteLayout.SetLayoutBounds(right, new Rect(1, 0, 0.5, 1));
		AbsoluteLayout.SetLayoutFlags(right, AbsoluteLayoutFlags.All);
		layout.Children.Add(right);

		// Centered label
		var label = new Label { Text = "Proportional (50/50)", TextColor = Colors.Black, FontAttributes = FontAttributes.Bold };
		AbsoluteLayout.SetLayoutBounds(label, new Rect(0.5, 0.5, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
		AbsoluteLayout.SetLayoutFlags(label, AbsoluteLayoutFlags.PositionProportional);
		layout.Children.Add(label);

		return layout;
	}
}
