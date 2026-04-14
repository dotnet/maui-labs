using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class FormattedTextPage : ContentPage
{
	public FormattedTextPage()
	{
		Title = "Formatted Text";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = 24,
				Spacing = 20,
				Children =
				{
					new Label { Text = "FormattedText Demos", FontSize = 28, FontAttributes = FontAttributes.Bold },

					// Basic bold/italic/normal
					SectionHeader("Bold, Italic & Mixed Styles"),
					MakeFormattedLabel(
						new Span { Text = "This is " },
						new Span { Text = "bold", FontAttributes = FontAttributes.Bold },
						new Span { Text = ", this is " },
						new Span { Text = "italic", FontAttributes = FontAttributes.Italic },
						new Span { Text = ", and this is " },
						new Span { Text = "bold italic", FontAttributes = FontAttributes.Bold | FontAttributes.Italic },
						new Span { Text = "." }
					),

					// Colors
					SectionHeader("Text & Background Colors"),
					MakeFormattedLabel(
						new Span { Text = "Red text ", TextColor = Colors.Red },
						new Span { Text = "Blue text ", TextColor = Colors.Blue },
						new Span { Text = "Green text ", TextColor = Colors.Green },
						new Span { Text = " Highlighted ", TextColor = Colors.White, BackgroundColor = Colors.DodgerBlue },
						new Span { Text = " Warning ", TextColor = Colors.Black, BackgroundColor = Colors.Gold }
					),

					// Font sizes
					SectionHeader("Font Sizes"),
					MakeFormattedLabel(
						new Span { Text = "Tiny ", FontSize = 10 },
						new Span { Text = "Small ", FontSize = 13 },
						new Span { Text = "Medium ", FontSize = 18 },
						new Span { Text = "Large ", FontSize = 24 },
						new Span { Text = "Huge", FontSize = 32 }
					),

					// Decorations
					SectionHeader("Text Decorations"),
					MakeFormattedLabel(
						new Span { Text = "Normal  " },
						new Span { Text = "Underlined  ", TextDecorations = TextDecorations.Underline },
						new Span { Text = "Strikethrough  ", TextDecorations = TextDecorations.Strikethrough },
						new Span { Text = "Both", TextDecorations = TextDecorations.Underline | TextDecorations.Strikethrough }
					),

					// Character spacing
					SectionHeader("Character Spacing"),
					MakeFormattedLabel(
						new Span { Text = "Normal spacing  " },
						new Span { Text = "Wide spacing", CharacterSpacing = 4 }
					),

					// Rich paragraph
					SectionHeader("Rich Paragraph"),
					MakeFormattedLabel(
						new Span { Text = "The ", FontSize = 15 },
						new Span { Text = "FormattedString", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.DodgerBlue },
						new Span { Text = " property lets you build ", FontSize = 15 },
						new Span { Text = "rich text", FontSize = 15, FontAttributes = FontAttributes.Italic, TextColor = Colors.OrangeRed },
						new Span { Text = " with ", FontSize = 15 },
						new Span { Text = "multiple spans", FontSize = 15, TextDecorations = TextDecorations.Underline },
						new Span { Text = ", each with its own ", FontSize = 15 },
						new Span { Text = "styling", FontSize = 15, FontAttributes = FontAttributes.Bold | FontAttributes.Italic, BackgroundColor = Colors.LightYellow },
						new Span { Text = ". This is rendered natively using ", FontSize = 15 },
						new Span { Text = "NSAttributedString", FontSize = 14, FontFamily = "Menlo", TextColor = Colors.Purple, BackgroundColor = Color.FromRgba(0.95, 0.92, 1.0, 1.0) },
						new Span { Text = " on macOS.", FontSize = 15 }
					),

					// Monospace / code-like
					SectionHeader("Code-Style Text"),
					MakeFormattedLabel(
						new Span { Text = "Use " },
						new Span { Text = "var x = 42;", FontFamily = "Menlo", FontSize = 13, TextColor = Colors.DarkGreen, BackgroundColor = Color.FromRgba(0.94, 0.94, 0.94, 1.0) },
						new Span { Text = " to declare a variable, or " },
						new Span { Text = "Console.WriteLine()", FontFamily = "Menlo", FontSize = 13, TextColor = Colors.DarkGreen, BackgroundColor = Color.FromRgba(0.94, 0.94, 0.94, 1.0) },
						new Span { Text = " to print output." }
					),
				}
			}
		};
	}

	static Label MakeFormattedLabel(params Span[] spans)
	{
		var fs = new FormattedString();
		foreach (var span in spans)
			fs.Spans.Add(span);
		return new Label { FormattedText = fs };
	}

	static Label SectionHeader(string title) => new Label
	{
		Text = title,
		FontSize = 18,
		FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 8, 0, 2),
	};
}
