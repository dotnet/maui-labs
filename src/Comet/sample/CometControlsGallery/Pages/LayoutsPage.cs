using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class LayoutsPage : View
	{
		[Body]
		View body() => GalleryPageHelpers.Scaffold("Layouts",
			GalleryPageHelpers.SectionHeader("VerticalStackLayout"),
			Border(
				VStack(6,
					ColorBlock("Item 1", Colors.CornflowerBlue),
					ColorBlock("Item 2", Colors.MediumSeaGreen),
					ColorBlock("Item 3", Colors.Coral)
				)
			)
			.StrokeColor(Colors.SlateGrey)
			.StrokeThickness(1)
			.CornerRadius(8)
			.Padding(new Thickness(12)),

			GalleryPageHelpers.SectionHeader("HorizontalStackLayout"),
			Border(
				HStack(8,
					ColorBlock("A", Colors.DodgerBlue, 80),
					ColorBlock("B", Colors.Orange, 80),
					ColorBlock("C", Colors.MediumPurple, 80),
					ColorBlock("D", Colors.Teal, 80)
				)
			)
			.StrokeColor(Colors.SlateGrey)
			.StrokeThickness(1)
			.CornerRadius(8)
			.Padding(new Thickness(12)),

			GalleryPageHelpers.SectionHeader("Nested Layouts"),
			Border(
				VStack(8,
					HStack(8,
						ColorBlock("Top-Left", Colors.Salmon)
							.FillHorizontal(),
						ColorBlock("Top-Right", Colors.SkyBlue)
							.FillHorizontal()
					),
					HStack(8,
						ColorBlock("Bottom-Left", Colors.PaleGreen)
							.FillHorizontal(),
						ColorBlock("Bottom-Right", Colors.Plum)
							.FillHorizontal()
					)
				)
			)
			.StrokeColor(Colors.SlateGrey)
			.StrokeThickness(1)
			.CornerRadius(8)
			.Padding(new Thickness(12)),

			GalleryPageHelpers.SectionHeader("Bordered Container"),
			Border(
				VStack(4,
					Text("Inside a Border")
						.FontSize(16)
						.FontWeight(FontWeight.Bold),
					Text("Borders provide a container with custom stroke and thickness.")
						.FontSize(13)
						.Color(Colors.Grey)
				)
			)
			.StrokeColor(Colors.DarkOrange)
			.StrokeThickness(1)
			.CornerRadius(8)
			.Padding(new Thickness(16)),

			GalleryPageHelpers.SectionHeader("Border"),
			Border(
				HStack(12,
					Text("Art").FontSize(32),
					VStack(4,
						Text("Styled Border").FontSize(16).FontWeight(FontWeight.Bold),
						Text("Borders can have custom stroke colors, thickness, and backgrounds").FontSize(13).Color(Colors.Gray)
					)
				)
			)
			.StrokeColor(Colors.MediumPurple)
			.StrokeThickness(2)
			.CornerRadius(8)
			.Padding(new Thickness(16)),

			GalleryPageHelpers.SectionHeader("Rounded Borders"),
			Border(
				Text("Uniform 12px corners")
					.FontSize(14)
			)
			.StrokeColor(Colors.DodgerBlue)
			.StrokeThickness(2)
			.CornerRadius(12)
			.Padding(new Thickness(16)),
			Border(
				Text("Asymmetric corners (20/4/20/4)")
					.FontSize(14)
			)
			.StrokeColor(Colors.MediumPurple)
			.StrokeThickness(2)
			.Background(Colors.MediumPurple.WithAlpha(0.1f))
			.CornerRadius(20, 4, 20, 4)
			.Padding(new Thickness(16)),
			Border(
				Text("Pill-style rounded")
					.FontSize(14)
					.Color(Colors.White)
			)
			.StrokeThickness(0)
			.Background(Colors.Teal)
			.CornerRadius(24)
			.Padding(new Thickness(20)),

			GalleryPageHelpers.SectionHeader("Deeply Nested"),
			Border(
				Border(
					Border(
						Border(
							Text("4 levels deep!")
								.FontSize(14)
						)
						.StrokeColor(Colors.Blue)
						.StrokeThickness(2)
						.CornerRadius(8)
						.Padding(new Thickness(12))
					)
					.StrokeColor(Colors.Green)
					.StrokeThickness(2)
					.CornerRadius(8)
					.Padding(new Thickness(8))
				)
				.StrokeColor(Colors.Orange)
				.StrokeThickness(2)
				.CornerRadius(8)
				.Padding(new Thickness(8))
			)
			.StrokeColor(Colors.Red)
			.StrokeThickness(2)
			.CornerRadius(8)
			.Padding(new Thickness(8))
		);

		static View ColorBlock(string text, Color bg, float width = 0)
		{
			var view = Border(
				Text(text)
					.Color(Colors.White)
					.FontSize(14)
					.FontWeight(FontWeight.Bold)
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.Background(bg)
			.StrokeThickness(0)
			.CornerRadius(6)
			.Padding(new Thickness(12, 8));

			if (width > 0)
				view = view.Frame(width: width);

			return view;
		}
	}
}
