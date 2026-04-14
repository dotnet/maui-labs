using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class CarouselViewPage : ContentPage
{
	public CarouselViewPage()
	{
		Title = "CarouselView";

		var slides = new List<SlideItem>
		{
			new("Welcome", "Swipe left and right to navigate between slides", Colors.DodgerBlue, "👋"),
			new("Features", "CarouselView supports paging, templates, and position tracking", Colors.MediumSeaGreen, "⚙️"),
			new("Templates", "Each slide uses a DataTemplate for custom content", Colors.MediumOrchid, "🎨"),
			new("Navigation", "Use the Previous/Next buttons or swipe to move", Colors.Coral, "🧭"),
			new("Complete", "You've reached the last slide!", Colors.SlateBlue, "✅"),
		};

		var positionLabel = new Label
		{
			Text = "Slide 1 of 5",
			FontSize = 14,
			TextColor = Colors.Gray,
			HorizontalTextAlignment = TextAlignment.Center,
		};

		var carousel = new CarouselView
		{
			HeightRequest = 300,
			ItemsSource = slides,
			ItemTemplate = new DataTemplate(() =>
			{
				var icon = new Label
				{
					FontSize = 48,
					HorizontalTextAlignment = TextAlignment.Center,
				};
				icon.SetBinding(Label.TextProperty, nameof(SlideItem.Icon));

				var title = new Label
				{
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					TextColor = Colors.White,
					HorizontalTextAlignment = TextAlignment.Center,
				};
				title.SetBinding(Label.TextProperty, nameof(SlideItem.Title));

				var desc = new Label
				{
					FontSize = 14,
					TextColor = Colors.White,
					HorizontalTextAlignment = TextAlignment.Center,
					Padding = new Thickness(20, 0),
				};
				desc.SetBinding(Label.TextProperty, nameof(SlideItem.Description));

				var container = new VerticalStackLayout
				{
					Spacing = 12,
					VerticalOptions = LayoutOptions.Center,
					HorizontalOptions = LayoutOptions.Center,
					Children = { icon, title, desc },
				};

				var border = new Border
				{
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
					StrokeThickness = 0,
					Padding = new Thickness(24),
					Content = container,
				};
				border.SetBinding(Border.BackgroundColorProperty, nameof(SlideItem.Color));

				return border;
			}),
		};

		var prevBtn = new Button { Text = "◀ Previous", FontSize = 13 };
		var nextBtn = new Button { Text = "Next ▶", FontSize = 13 };

		prevBtn.Clicked += (s, e) =>
		{
			if (carousel.Position > 0)
				carousel.Position--;
		};

		nextBtn.Clicked += (s, e) =>
		{
			if (carousel.Position < slides.Count - 1)
				carousel.Position++;
		};

		carousel.PositionChanged += (s, e) =>
		{
			positionLabel.Text = $"Slide {e.CurrentPosition + 1} of {slides.Count}";
		};

		var navRow = new HorizontalStackLayout
		{
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 12,
			Children = { prevBtn, nextBtn },
		};

		// Dots indicator
		var dotsLayout = new HorizontalStackLayout
		{
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 8,
		};
		var dots = new List<Border>();
		for (int i = 0; i < slides.Count; i++)
		{
			var dot = new Border
			{
				WidthRequest = 10,
				HeightRequest = 10,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
				StrokeThickness = 0,
				BackgroundColor = i == 0 ? Colors.DodgerBlue : Colors.Gray,
			};
			dots.Add(dot);
			dotsLayout.Children.Add(dot);
		}

		carousel.PositionChanged += (s, e) =>
		{
			for (int i = 0; i < dots.Count; i++)
				dots[i].BackgroundColor = i == e.CurrentPosition ? Colors.DodgerBlue : Colors.Gray;
		};

		Content = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(24),
			Children =
			{
				carousel,
				dotsLayout,
				positionLabel,
				navRow,
			}
		};
	}

	class SlideItem
	{
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public Color Color { get; set; } = Colors.Gray;
		public string Icon { get; set; } = "";

		public SlideItem(string title, string description, Color color, string icon)
		{
			Title = title;
			Description = description;
			Color = color;
			Icon = icon;
		}
	}
}
