#nullable enable

using System.Windows.Controls;
using System.Windows.Documents;
using WColor = System.Windows.Media.Color;
using WBrush = System.Windows.Media.SolidColorBrush;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class LabelHandler : WPFViewHandler<ILabel, TextBlock>
	{
		protected override TextBlock CreatePlatformView() => new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };

		static WBrush? ToBrush(Graphics.Color? color)
		{
			if (color == null)
				return null;
			return new WBrush(WColor.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapText(LabelHandler handler, ILabel label) =>
			handler.PlatformView.Text = label.Text;

		public static void MapTextColor(LabelHandler handler, ILabel label)
		{
			var brush = ToBrush(label.TextColor);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
			else
			{
				// Apply theme-appropriate default when no explicit TextColor
				var app = Microsoft.Maui.Controls.Application.Current;
				bool dark = app?.RequestedTheme == AppTheme.Dark;
				handler.PlatformView.Foreground = dark
					? new WBrush(WColor.FromRgb(255, 255, 255))
					: new WBrush(WColor.FromRgb(26, 26, 26));
			}
		}

		public static void MapFont(LabelHandler handler, ILabel label)
		{
			if (label.Font.Size > 0)
				handler.PlatformView.FontSize = label.Font.Size;

			handler.PlatformView.FontWeight = label.Font.Weight >= FontWeight.Bold
				? System.Windows.FontWeights.Bold
				: System.Windows.FontWeights.Normal;

			handler.PlatformView.FontStyle =
				(label.Font.Slant == FontSlant.Italic || label.Font.Slant == FontSlant.Oblique)
					? System.Windows.FontStyles.Italic
					: System.Windows.FontStyles.Normal;

			if (!string.IsNullOrEmpty(label.Font.Family))
				handler.PlatformView.FontFamily = new System.Windows.Media.FontFamily(label.Font.Family);
		}

		public static void MapCharacterSpacing(LabelHandler handler, ILabel label)
		{
			// WPF doesn't have a direct CharacterSpacing property on TextBlock.
			// Character spacing is only available through OpenType typography features
			// which depends on font support. Best effort: no-op for unsupported fonts.
			// MAUI WinUI uses TextElement.CharacterSpacing which is a WinUI-specific API.
		}

		public static void MapHorizontalTextAlignment(LabelHandler handler, ILabel label)
		{
			handler.PlatformView.TextAlignment = label.HorizontalTextAlignment switch
			{
				TextAlignment.Center => System.Windows.TextAlignment.Center,
				TextAlignment.End => System.Windows.TextAlignment.Right,
				_ => System.Windows.TextAlignment.Left,
			};
		}

		public static void MapVerticalTextAlignment(LabelHandler handler, ILabel label)
		{
			handler.PlatformView.VerticalAlignment = label.VerticalTextAlignment switch
			{
				TextAlignment.Center => System.Windows.VerticalAlignment.Center,
				TextAlignment.End => System.Windows.VerticalAlignment.Bottom,
				_ => System.Windows.VerticalAlignment.Top,
			};
		}

		public static void MapTextDecorations(LabelHandler handler, ILabel label)
		{
			handler.PlatformView.TextDecorations = null;

			if (label.TextDecorations.HasFlag(TextDecorations.Underline))
				handler.PlatformView.TextDecorations = System.Windows.TextDecorations.Underline;

			if (label.TextDecorations.HasFlag(TextDecorations.Strikethrough))
			{
				// System.Windows.TextDecorations.Underline/Strikethrough are frozen/shared
				// collections — must copy into a new mutable one before adding.
				var decorations = handler.PlatformView.TextDecorations != null
					? new System.Windows.TextDecorationCollection(handler.PlatformView.TextDecorations)
					: new System.Windows.TextDecorationCollection();
				foreach (var d in System.Windows.TextDecorations.Strikethrough)
					decorations.Add(d);
				handler.PlatformView.TextDecorations = decorations;
			}
		}

		public static void MapPadding(LabelHandler handler, ILabel label)
		{
			handler.PlatformView.Padding = new System.Windows.Thickness(
				label.Padding.Left, label.Padding.Top,
				label.Padding.Right, label.Padding.Bottom);
		}

		public static void MapLineHeight(LabelHandler handler, ILabel label)
		{
			if (label.LineHeight > 0)
				handler.PlatformView.LineHeight = label.LineHeight * handler.PlatformView.FontSize;
		}

		public static void MapMaxLines(LabelHandler handler, ILabel label)
		{
			if (label is Microsoft.Maui.Controls.Label mauiLabel && mauiLabel.MaxLines > 0)
			{
				handler.PlatformView.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
				handler.PlatformView.TextWrapping = System.Windows.TextWrapping.Wrap;
			}
			else
			{
				handler.PlatformView.TextTrimming = System.Windows.TextTrimming.None;
			}
		}

		public static void MapFormattedText(LabelHandler handler, ILabel label)
		{
			if (label is not Microsoft.Maui.Controls.Label mauiLabel)
				return;

			var formattedText = mauiLabel.FormattedText;
			if (formattedText == null || formattedText.Spans.Count == 0)
				return;

			handler.PlatformView.Inlines.Clear();
			handler.PlatformView.Text = null;

			foreach (var mauiSpan in formattedText.Spans)
			{
				var run = new Run { Text = mauiSpan.Text ?? string.Empty };

				var foreground = ToBrush(mauiSpan.TextColor);
				if (foreground != null)
					run.Foreground = foreground;

				if (mauiSpan.BackgroundColor != null)
				{
					var background = ToBrush(mauiSpan.BackgroundColor);
					if (background != null)
						run.Background = background;
				}

				if (mauiSpan.FontSize > 0)
					run.FontSize = mauiSpan.FontSize;

				if (mauiSpan.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Bold))
					run.FontWeight = System.Windows.FontWeights.Bold;

				if (mauiSpan.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Italic))
					run.FontStyle = System.Windows.FontStyles.Italic;

				if (!string.IsNullOrEmpty(mauiSpan.FontFamily))
					run.FontFamily = new System.Windows.Media.FontFamily(mauiSpan.FontFamily);

				// CharacterSpacing: WPF doesn't support per-run character spacing natively

				if (mauiSpan.TextDecorations.HasFlag(TextDecorations.Underline))
					run.TextDecorations = System.Windows.TextDecorations.Underline;

				if (mauiSpan.TextDecorations.HasFlag(TextDecorations.Strikethrough))
				{
					// Copy into a new mutable collection — the static WPF collections are frozen.
					var decorations = run.TextDecorations != null
						? new System.Windows.TextDecorationCollection(run.TextDecorations)
						: new System.Windows.TextDecorationCollection();
					foreach (var d in System.Windows.TextDecorations.Strikethrough)
						decorations.Add(d);
					run.TextDecorations = decorations;
				}

				handler.PlatformView.Inlines.Add(run);
			}
		}

		public static void MapLineBreakMode(LabelHandler handler, ILabel label)
		{
			if (label is not Microsoft.Maui.Controls.Label mauiLabel)
				return;

			switch (mauiLabel.LineBreakMode)
			{
				case LineBreakMode.NoWrap:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.None;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case LineBreakMode.WordWrap:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.None;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.Wrap;
					break;
				case LineBreakMode.CharacterWrap:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.Wrap;
					break;
				case LineBreakMode.HeadTruncation:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.WordEllipsis;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case LineBreakMode.TailTruncation:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case LineBreakMode.MiddleTruncation:
					handler.PlatformView.TextTrimming = System.Windows.TextTrimming.WordEllipsis;
					handler.PlatformView.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
			}
		}
	}
}
