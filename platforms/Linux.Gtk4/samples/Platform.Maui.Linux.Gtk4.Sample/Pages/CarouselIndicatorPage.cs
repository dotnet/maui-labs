using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates CarouselView with IndicatorView for page-style navigation.
/// </summary>
public class CarouselIndicatorPage : ContentPage
{
	public CarouselIndicatorPage()
	{
		Title = "Carousel & Indicators";

		var items = new ObservableCollection<CarouselItem>
		{
			new("🌄 Welcome", "Swipe through slides to see the carousel in action.", Colors.LightBlue),
			new("🎨 Design", "The IndicatorView shows dot indicators synced to position.", Colors.LightGreen),
			new("⚡ Performance", "CarouselView supports horizontal and vertical layouts.", Colors.LightYellow),
			new("🚀 Deploy", "Ready for production? Build and ship your Linux app!", Colors.LightPink),
		};

		var carousel = new CarouselView
		{
			ItemsSource = items,
			HeightRequest = 200,
			Loop = false,
			ItemTemplate = new DataTemplate(() =>
			{
				var frame = new Frame
				{
					CornerRadius = 12,
					Padding = new Thickness(24),
					Margin = new Thickness(8),
					HasShadow = true,
				};
				frame.SetBinding(VisualElement.BackgroundColorProperty, "Color");

				var stack = new StackLayout
				{
					VerticalOptions = LayoutOptions.Center,
					HorizontalOptions = LayoutOptions.Center,
					Spacing = 12,
				};

				var title = new Label
				{
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				};
				title.SetBinding(Label.TextProperty, "Title");

				var desc = new Label
				{
					FontSize = 14,
					HorizontalTextAlignment = TextAlignment.Center,
					TextColor = Colors.DimGray,
				};
				desc.SetBinding(Label.TextProperty, "Description");

				stack.Children.Add(title);
				stack.Children.Add(desc);
				frame.Content = stack;
				return frame;
			}),
		};

		var indicator = new IndicatorView
		{
			IndicatorColor = Colors.LightGray,
			SelectedIndicatorColor = Colors.DodgerBlue,
			IndicatorSize = 10,
			HorizontalOptions = LayoutOptions.Center,
			Margin = new Thickness(0, 8),
			Count = items.Count,
		};

		// Sync carousel position to indicator
		carousel.PositionChanged += (s, e) =>
		{
			indicator.Position = e.CurrentPosition;
		};

		// Manual position controls
		var prevBtn = new Button { Text = "◀ Prev" };
		var nextBtn = new Button { Text = "Next ▶" };
		var posLabel = new Label
		{
			Text = "Position: 0",
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
		};

		prevBtn.Clicked += (s, e) =>
		{
			if (carousel.Position > 0)
			{
				carousel.Position--;
				indicator.Position = carousel.Position;
				posLabel.Text = $"Position: {carousel.Position}";
			}
		};
		nextBtn.Clicked += (s, e) =>
		{
			if (carousel.Position < items.Count - 1)
			{
				carousel.Position++;
				indicator.Position = carousel.Position;
				posLabel.Text = $"Position: {carousel.Position}";
			}
		};

		var controls = new HorizontalStackLayout
		{
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 16,
			Children = { prevBtn, posLabel, nextBtn },
		};

		// Shape variants section
		var squareIndicator = new IndicatorView
		{
			IndicatorColor = Colors.Silver,
			SelectedIndicatorColor = Colors.OrangeRed,
			IndicatorsShape = IndicatorShape.Square,
			IndicatorSize = 12,
			Count = items.Count,
			HorizontalOptions = LayoutOptions.Center,
			Margin = new Thickness(0, 4),
		};

		carousel.PositionChanged += (s, e) =>
		{
			squareIndicator.Position = e.CurrentPosition;
		};

		Content = new ScrollView
		{
			Content = new StackLayout
			{
				Padding = new Thickness(16),
				Spacing = 12,
				Children =
				{
					new Label
					{
						Text = "CarouselView + IndicatorView",
						FontSize = 22,
						FontAttributes = FontAttributes.Bold,
					},
					carousel,
					new Label { Text = "Circle indicators:", FontAttributes = FontAttributes.Bold },
					indicator,
					new Label { Text = "Square indicators:", FontAttributes = FontAttributes.Bold },
					squareIndicator,
					new BoxView { HeightRequest = 8 },
					controls,
				}
			}
		};
	}

	record CarouselItem(string Title, string Description, Color Color)
	{
		public override string ToString() => $"{Title}\n{Description}";
	}
}
