using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Text;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class LabelHandler : GtkViewHandler<ILabel, Gtk.Label>
{
	public static new IPropertyMapper<ILabel, LabelHandler> Mapper =
		new PropertyMapper<ILabel, LabelHandler>(ViewMapper)
		{
			[nameof(ILabel.Text)] = MapText,
			[nameof(ILabel.TextColor)] = MapTextColor,
			[nameof(ILabel.Font)] = MapFont,
			[nameof(ILabel.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(ILabel.Padding)] = MapPadding,
			[nameof(ILabel.TextDecorations)] = MapTextDecorations,
			[nameof(ILabel.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(ILabel.LineHeight)] = MapLineHeight,
			[nameof(ITextAlignment.VerticalTextAlignment)] = MapVerticalTextAlignment,
			["FormattedText"] = MapFormattedText,
		};

	public LabelHandler() : base(Mapper)
	{
	}

	protected override Gtk.Label CreatePlatformView()
	{
		var label = Gtk.Label.New(string.Empty);
		label.SetWrap(true);
		label.SetXalign(0);
		return label;
	}

	public static void MapText(LabelHandler handler, ILabel label)
	{
		if (handler.PlatformView == null) return;

		// If FormattedText is set, prefer that
		if (label is Label mauiLabel && mauiLabel.FormattedText?.Spans.Count > 0)
			return;

		handler.PlatformView.SetText(label.Text ?? string.Empty);
	}

	public static void MapTextColor(LabelHandler handler, ILabel label)
	{
		if (label.TextColor != null)
		{
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(label.TextColor)};");
		}
	}

	public static void MapFont(LabelHandler handler, ILabel label)
	{
		var css = handler.BuildFontCss(label.Font);

		if (!string.IsNullOrEmpty(css))
			handler.ApplyCss(handler.PlatformView, css);
	}

	public static void MapHorizontalTextAlignment(LabelHandler handler, ILabel label)
	{
		handler.PlatformView?.SetXalign(label.HorizontalTextAlignment switch
		{
			TextAlignment.Start => 0f,
			TextAlignment.Center => 0.5f,
			TextAlignment.End => 1f,
			_ => 0f
		});
	}

	public static void MapPadding(LabelHandler handler, ILabel label)
	{
		var padding = label.Padding;
		handler.PlatformView?.SetMarginStart((int)padding.Left);
		handler.PlatformView?.SetMarginEnd((int)padding.Right);
		handler.PlatformView?.SetMarginTop((int)padding.Top);
		handler.PlatformView?.SetMarginBottom((int)padding.Bottom);
	}

	public static void MapTextDecorations(LabelHandler handler, ILabel label)
	{
		if (label.TextDecorations.HasFlag(TextDecorations.Underline))
		{
			handler.ApplyCss(handler.PlatformView, "text-decoration: underline;");
		}
		else if (label.TextDecorations.HasFlag(TextDecorations.Strikethrough))
		{
			handler.ApplyCss(handler.PlatformView, "text-decoration: line-through;");
		}
	}

	public static void MapCharacterSpacing(LabelHandler handler, ILabel label)
	{
		handler.ApplyCss(handler.PlatformView, $"letter-spacing: {label.CharacterSpacing}px;");
	}

	public static void MapLineHeight(LabelHandler handler, ILabel label)
	{
		if (label.LineHeight > 0)
			handler.ApplyCss(handler.PlatformView, $"line-height: {label.LineHeight};");
	}

	public static void MapVerticalTextAlignment(LabelHandler handler, ILabel label)
	{
		if (handler.PlatformView == null) return;
		handler.PlatformView.SetYalign(label.VerticalTextAlignment switch
		{
			TextAlignment.Start => 0f,
			TextAlignment.Center => 0.5f,
			TextAlignment.End => 1f,
			_ => 0.5f
		});
	}

	public static void MapFormattedText(LabelHandler handler, ILabel label)
	{
		if (handler.PlatformView == null) return;

		if (label is not Label mauiLabel || mauiLabel.FormattedText == null || mauiLabel.FormattedText.Spans.Count == 0)
		{
			// No formatted text — fall back to plain text
			MapText(handler, label);
			return;
		}

		var markup = BuildPangoMarkup(mauiLabel.FormattedText);
		handler.PlatformView.SetMarkup(markup);
	}

	static string BuildPangoMarkup(FormattedString formatted)
	{
		var sb = new StringBuilder();

		foreach (var span in formatted.Spans)
		{
			if (string.IsNullOrEmpty(span.Text))
				continue;

			var attrs = new StringBuilder();

			// Text color
			if (span.TextColor != null)
			{
				var c = span.TextColor;
				attrs.Append($" foreground=\"{ToGtkColorHex(c)}\"");
			}

			// Background color
			if (span.BackgroundColor != null)
			{
				var c = span.BackgroundColor;
				attrs.Append($" background=\"{ToGtkColorHex(c)}\"");
			}

			// Font size (Pango uses points * 1024 for "size" or "XX" units)
			if (span.FontSize > 0)
			{
				var sizeInPangoUnits = (int)(span.FontSize * 1024);
				attrs.Append($" size=\"{sizeInPangoUnits}\"");
			}

			// Font family
			if (!string.IsNullOrEmpty(span.FontFamily))
			{
				attrs.Append($" font_family=\"{GLib.Functions.MarkupEscapeText(span.FontFamily, -1)}\"");
			}

			// Font attributes (bold, italic)
			if (span.FontAttributes.HasFlag(FontAttributes.Bold))
				attrs.Append(" weight=\"bold\"");
			if (span.FontAttributes.HasFlag(FontAttributes.Italic))
				attrs.Append(" style=\"italic\"");

			// Text decorations
			if (span.TextDecorations.HasFlag(TextDecorations.Underline))
				attrs.Append(" underline=\"single\"");
			if (span.TextDecorations.HasFlag(TextDecorations.Strikethrough))
				attrs.Append(" strikethrough=\"true\"");

			// Character spacing (Pango letter_spacing in 1/1024 pt)
			if (span.CharacterSpacing > 0)
			{
				var spacing = (int)(span.CharacterSpacing * 1024);
				attrs.Append($" letter_spacing=\"{spacing}\"");
			}

			var escapedText = GLib.Functions.MarkupEscapeText(span.Text, -1);

			if (attrs.Length > 0)
				sb.Append($"<span{attrs}>{escapedText}</span>");
			else
				sb.Append(escapedText);
		}

		return sb.ToString();
	}

	static string ToGtkColorHex(Color c)
	{
		return $"#{(int)(c.Red * 255):X2}{(int)(c.Green * 255):X2}{(int)(c.Blue * 255):X2}";
	}
}
