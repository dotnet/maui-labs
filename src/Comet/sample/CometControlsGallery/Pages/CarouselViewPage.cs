using System;
using System.Collections.Generic;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	record SlideItem(string Title, string Description, Color Color, string Icon);

	public class CarouselViewPage : View
	{
		static readonly List<SlideItem> Slides = new()
		{
			new("Welcome", "Swipe left and right to navigate between slides", Colors.DodgerBlue, "Wave"),
			new("Features", "CarouselView supports paging, templates, and position tracking", Colors.MediumSeaGreen, "Gear"),
			new("Templates", "Each slide uses a DataTemplate for custom content", Colors.MediumOrchid, "Palette"),
			new("Navigation", "Use the Previous/Next buttons or swipe to move", Colors.Coral, "Compass"),
			new("Complete", "You've reached the last slide!", Colors.SlateBlue, "Check"),
		};

		readonly Reactive<int> position = 0;

		[Body]
		View body()
		{
			return GalleryPageHelpers.Scaffold("CarouselView",
				// Carousel
				new CarouselView<SlideItem>(() => Slides)
				{
					ViewFor = slide =>
						Border(
							VStack(12,
								Text(slide.Icon)
									.FontSize(48)
									.HorizontalTextAlignment(TextAlignment.Center)
									.Color(Colors.White),
								Text(slide.Title)
									.FontSize(24)
									.FontWeight(FontWeight.Bold)
									.Color(Colors.White)
									.HorizontalTextAlignment(TextAlignment.Center),
								Text(slide.Description)
									.FontSize(14)
									.Color(Colors.White)
									.HorizontalTextAlignment(TextAlignment.Center)
									.Padding(new Thickness(20, 0))
							)
							.Padding(new Thickness(24))
							.VerticalLayoutAlignment(LayoutAlignment.Center)
						)
						.Background(slide.Color)
						.CornerRadius(12)
						.StrokeThickness(0),
					Position = position,
					PositionChanged = pos =>
					{
						// Guard: only write when value actually changes to prevent
						// body rebuild -> carousel recreate -> flash-back loop
						if (pos != position.Value)
							position.Value = pos;
					},
				}
				.Frame(height: 300),

				// Dots indicator
				BuildDots(),

				// Position label
				Text(() => $"Slide {position.Value + 1} of {Slides.Count}")
					.FontSize(14)
					.Color(Colors.Gray)
					.HorizontalTextAlignment(TextAlignment.Center),

				// Navigation buttons
				HStack(12,
					Button("◀ Previous", () =>
					{
						if (position.Value > 0)
							position.Value--;
					})
					.FontSize(13),
					Button("Next", () =>
					{
						if (position.Value < Slides.Count - 1)
							position.Value++;
					})
					.FontSize(13)
				)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
			);
		}

		View BuildDots()
		{
			var dots = new List<View>();
			for (int i = 0; i < Slides.Count; i++)
			{
				var isActive = i == position.Value;
				dots.Add(
					new Border()
						.Frame(10, 10)
						.CornerRadius(5)
						.StrokeThickness(0)
						.Background(isActive ? Colors.Blue : Colors.LightGray)
				);
			}
			return HStack(8, dots.ToArray())
				.HorizontalLayoutAlignment(LayoutAlignment.Center);
		}
	}
}
