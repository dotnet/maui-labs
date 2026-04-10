using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// Represents a span of styled text within a FormattedText.
	/// Matches MAUI's Span class for rich text formatting.
	/// </summary>
	public class Span
	{
		public string Text { get; set; } = "";
		public Color TextColor { get; set; }
		public string FontFamily { get; set; }
		public double FontSize { get; set; } = -1;
		public FontAttributes FontAttributes { get; set; } = FontAttributes.None;
		public TextDecorations TextDecorations { get; set; } = TextDecorations.None;
		public Color BackgroundColor { get; set; }
		public double CharacterSpacing { get; set; }
		public double LineHeight { get; set; } = -1;

		public Span() { }
		public Span(string text) { Text = text; }

		public Span Bold() { FontAttributes |= FontAttributes.Bold; return this; }
		public Span Italic() { FontAttributes |= FontAttributes.Italic; return this; }
		public Span Underline() { TextDecorations |= TextDecorations.Underline; return this; }
		public Span Strikethrough() { TextDecorations |= TextDecorations.Strikethrough; return this; }
		public Span Color(Color color) { TextColor = color; return this; }
		public Span Font(string family) { FontFamily = family; return this; }
		public Span Size(double size) { FontSize = size; return this; }
		public Span Background(Color color) { BackgroundColor = color; return this; }
		public Span Spacing(double spacing) { CharacterSpacing = spacing; return this; }
		public Span Height(double height) { LineHeight = height; return this; }

		/// <summary>
		/// Converts to a MAUI Controls Span.
		/// </summary>
		public Microsoft.Maui.Controls.Span ToMauiSpan()
		{
			var span = new Microsoft.Maui.Controls.Span { Text = Text };

			if (TextColor is not null)
				span.TextColor = TextColor;
			if (FontFamily is not null)
				span.FontFamily = FontFamily;
			if (FontSize > 0)
				span.FontSize = FontSize;
			if (FontAttributes != FontAttributes.None)
				span.FontAttributes = (Microsoft.Maui.Controls.FontAttributes)(int)FontAttributes;
			if (TextDecorations != TextDecorations.None)
				span.TextDecorations = (Microsoft.Maui.TextDecorations)(int)TextDecorations;
			if (BackgroundColor is not null)
				span.BackgroundColor = BackgroundColor;
			if (CharacterSpacing != 0)
				span.CharacterSpacing = CharacterSpacing;
			if (LineHeight > 0)
				span.LineHeight = LineHeight;

			return span;
		}
	}

	/// <summary>
	/// Font attributes matching MAUI's FontAttributes enum.
	/// </summary>
	[Flags]
	public enum FontAttributes
	{
		None = 0,
		Bold = 1,
		Italic = 2,
	}

	/// <summary>
	/// Text decorations matching MAUI's TextDecorations enum.
	/// </summary>
	[Flags]
	public enum TextDecorations
	{
		None = 0,
		Underline = 1,
		Strikethrough = 2,
	}

	/// <summary>
	/// Represents formatted text with multiple styled spans.
	/// Provides MVU-friendly API for creating rich text matching MAUI's FormattedString.
	/// </summary>
	public class FormattedString
	{
		readonly List<Span> _spans = new();

		public IReadOnlyList<Span> Spans => _spans;

		public FormattedString() { }

		public FormattedString(params Span[] spans)
		{
			_spans.AddRange(spans);
		}

		public FormattedString Add(Span span)
		{
			_spans.Add(span);
			return this;
		}

		public FormattedString Add(string text)
		{
			_spans.Add(new Span(text));
			return this;
		}

		/// <summary>
		/// Converts to a MAUI Controls FormattedString for use with Label.FormattedText.
		/// </summary>
		public Microsoft.Maui.Controls.FormattedString ToMauiFormattedString()
		{
			var fs = new Microsoft.Maui.Controls.FormattedString();
			foreach (var span in _spans)
				fs.Spans.Add(span.ToMauiSpan());
			return fs;
		}

		/// <summary>
		/// Creates a MauiViewHost Label with this formatted text.
		/// </summary>
		public MauiViewHost ToView()
		{
			var label = new Microsoft.Maui.Controls.Label
			{
				FormattedText = ToMauiFormattedString()
			};
			return new MauiViewHost(label);
		}
	}

	/// <summary>
	/// Extension methods for creating formatted/rich text in MVU.
	/// </summary>
	public static class FormattedTextExtensions
	{
		/// <summary>
		/// Creates a FormattedString with the builder pattern.
		/// Usage: FormattedText.Create().Add(new Span("Hello ").Bold()).Add(new Span("World").Color(Colors.Red))
		/// </summary>
		public static FormattedString Create() => new FormattedString();
	}
}
