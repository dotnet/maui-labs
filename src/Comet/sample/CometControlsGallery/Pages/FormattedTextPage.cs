using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class FormattedTextPage : View
	{
		[Body]
		View body() => GalleryPageHelpers.Scaffold("Formatted Text",
			Text("FormattedText Demos")
				.FontSize(28)
				.FontWeight(FontWeight.Bold),

			GalleryPageHelpers.SectionHeader("Bold, Italic & Mixed Styles"),
			new FormattedString(
				new Span("This is "),
				new Span("bold").Bold(),
				new Span(", this is "),
				new Span("italic").Italic(),
				new Span(", and this is "),
				new Span("bold italic").Bold().Italic(),
				new Span(".")
			).ToView(),

			GalleryPageHelpers.SectionHeader("Text & Background Colors"),
			new FormattedString(
				new Span("Red text ").Color(Colors.Red),
				new Span("Blue text ").Color(Colors.Blue),
				new Span("Green text ").Color(Colors.Green),
				new Span(" Highlighted ").Color(Colors.White).Background(Colors.DodgerBlue),
				new Span(" Warning ").Color(Colors.Black).Background(Colors.Gold)
			).ToView(),

			GalleryPageHelpers.SectionHeader("Font Sizes"),
			new FormattedString(
				new Span("Tiny ").Size(10),
				new Span("Small ").Size(13),
				new Span("Medium ").Size(18),
				new Span("Large ").Size(24),
				new Span("Huge").Size(32)
			).ToView(),

			GalleryPageHelpers.SectionHeader("Text Decorations"),
			new FormattedString(
				new Span("Normal  "),
				new Span("Underlined  ").Underline(),
				new Span("Strikethrough  ").Strikethrough(),
				new Span("Both").Underline().Strikethrough()
			).ToView(),

			GalleryPageHelpers.SectionHeader("Character Spacing"),
			new FormattedString(
				new Span("Normal spacing  "),
				new Span("Wide spacing").Spacing(4)
			).ToView(),

			GalleryPageHelpers.SectionHeader("Rich Paragraph"),
			new FormattedString(
				new Span("The ").Size(15),
				new Span("FormattedString").Size(15).Bold().Color(Colors.DodgerBlue),
				new Span(" property lets you build ").Size(15),
				new Span("rich text").Size(15).Italic().Color(Colors.OrangeRed),
				new Span(" with ").Size(15),
				new Span("multiple spans").Size(15).Underline(),
				new Span(", each with its own ").Size(15),
				new Span("styling").Size(15).Bold().Italic().Background(Colors.LightYellow),
				new Span(". This is rendered natively using ").Size(15),
				new Span("NSAttributedString").Size(14).Font("Menlo").Color(Colors.Purple)
					.Background(Color.FromRgba(0.95, 0.92, 1.0, 1.0)),
				new Span(DeviceInfo.Platform == DevicePlatform.iOS ? " on iOS." : " on macOS.").Size(15)
			).ToView(),

			GalleryPageHelpers.SectionHeader("Code-Style Text"),
			new FormattedString(
				new Span("Use "),
				new Span("var x = 42;").Font("Menlo").Size(13).Color(Colors.DarkGreen)
					.Background(Color.FromRgba(0.94, 0.94, 0.94, 1.0)),
				new Span(" to declare a variable, or "),
				new Span("Console.WriteLine()").Font("Menlo").Size(13).Color(Colors.DarkGreen)
					.Background(Color.FromRgba(0.94, 0.94, 0.94, 1.0)),
				new Span(" to print output.")
			).ToView()
		);
	}
}
