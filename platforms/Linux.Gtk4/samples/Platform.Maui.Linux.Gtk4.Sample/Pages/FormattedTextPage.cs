using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class FormattedTextPage : ContentPage
{
	public FormattedTextPage()
	{
		Title = "FormattedText / Spans";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 20,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "FormattedText & Spans", FontSize = 22, FontAttributes = FontAttributes.Bold },

					// Basic styled spans
					BuildBasicSpans(),

					// Mixed font sizes
					BuildMixedSizes(),

					// Text decorations
					BuildDecorations(),

					// Colored spans
					BuildColoredSpans(),

					// Paragraph-like formatted text
					BuildParagraph(),
				}
			}
		};
	}

	static Label BuildBasicSpans()
	{
		var label = new Label();
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = "Bold ", FontAttributes = FontAttributes.Bold },
				new Span { Text = "Italic ", FontAttributes = FontAttributes.Italic },
				new Span { Text = "Bold+Italic ", FontAttributes = FontAttributes.Bold | FontAttributes.Italic },
				new Span { Text = "Normal" },
			}
		};
		return label;
	}

	static Label BuildMixedSizes()
	{
		var label = new Label();
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = "Small ", FontSize = 10 },
				new Span { Text = "Medium ", FontSize = 16 },
				new Span { Text = "Large ", FontSize = 24 },
				new Span { Text = "Huge", FontSize = 32 },
			}
		};
		return label;
	}

	static Label BuildDecorations()
	{
		var label = new Label();
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = "Underlined ", TextDecorations = TextDecorations.Underline },
				new Span { Text = "Strikethrough ", TextDecorations = TextDecorations.Strikethrough },
				new Span { Text = "Both", TextDecorations = TextDecorations.Underline | TextDecorations.Strikethrough },
			}
		};
		return label;
	}

	static Label BuildColoredSpans()
	{
		var label = new Label();
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = "Red ", TextColor = Colors.Tomato, FontAttributes = FontAttributes.Bold },
				new Span { Text = "Blue ", TextColor = Colors.DodgerBlue, FontAttributes = FontAttributes.Bold },
				new Span { Text = "Green ", TextColor = Colors.MediumSeaGreen, FontAttributes = FontAttributes.Bold },
				new Span { Text = "Gold ", TextColor = Colors.Gold, BackgroundColor = Colors.DarkSlateGray, FontAttributes = FontAttributes.Bold },
				new Span { Text = "Purple", TextColor = Colors.Orchid, FontAttributes = FontAttributes.Bold },
			}
		};
		return label;
	}

	static Label BuildParagraph()
	{
		var label = new Label();
		label.FormattedText = new FormattedString
		{
			Spans =
			{
				new Span { Text = "Platform.Maui.Linux.Gtk4 ", FontAttributes = FontAttributes.Bold, TextColor = Colors.DodgerBlue, FontSize = 16 },
				new Span { Text = "is a community backend for " },
				new Span { Text = ".NET MAUI", FontAttributes = FontAttributes.Bold },
				new Span { Text = " targeting " },
				new Span { Text = "Linux", FontAttributes = FontAttributes.Italic, TextColor = Colors.MediumSeaGreen },
				new Span { Text = " using " },
				new Span { Text = "GTK4", TextDecorations = TextDecorations.Underline, FontAttributes = FontAttributes.Bold, TextColor = Colors.Orchid },
				new Span { Text = ". This label demonstrates rich text with " },
				new Span { Text = "multiple spans", BackgroundColor = Colors.Gold, TextColor = Colors.Black },
				new Span { Text = " rendered via Pango markup." },
			}
		};
		return label;
	}
}
