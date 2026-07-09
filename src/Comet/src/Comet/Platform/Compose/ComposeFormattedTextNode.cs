#nullable enable
#if ANDROID
using System.Collections.Generic;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeAnnotatedText = AndroidX.Compose.AnnotatedText;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.FormattedText"/> as a Compose
	/// <c>AnnotatedText</c>: one wrapping paragraph of styled runs (color / monospace "code" /
	/// background / underline). Measured with a native SpannableString carrying the same monospace
	/// spans so the wrapped height matches the rendered glyph metrics.</summary>
	sealed class ComposeFormattedTextNode : ComposeNode
	{
		protected override bool PadsOwnContent => true;

		IReadOnlyList<TextRun> _runs = System.Array.Empty<TextRun>();
		int _fontSize;
		int _lineHeight;
		int _lineBreak;
		string? _fontFamily;
		readonly MutableState<int> _version = new(0);

		int EffectiveLineHeightSp() => _lineHeight > 0 ? _lineHeight : TextMeasure.LineHeightSp(_fontSize > 0 ? _fontSize : 16f);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Runs)
			{
				_runs = value.AsObject as IReadOnlyList<TextRun> ?? System.Array.Empty<TextRun>();
				_version.Value++;
			}
			else if (id == PropertyIds.Text_FontSize)
			{
				_fontSize = (int)System.Math.Round(value.AsDouble);
				_version.Value++;
			}
			else if (id == PropertyIds.Text_LineHeight)
			{
				_lineHeight = (int)System.Math.Round(value.AsDouble);
				_version.Value++;
			}
			else if (id == PropertyIds.Text_LineBreak)
			{
				_lineBreak = value.AsInt;
				_version.Value++;
			}
			else if (id == PropertyIds.Text_FontFamily)
			{
				_fontFamily = value.AsString;
				_version.Value++;
			}
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			int sp = _fontSize > 0 ? _fontSize : 16;
			var baseTypeface = ComposeFontRegistry.Resolve(_fontFamily, 400)?.Typeface;
			return TextMeasure.MeasureRuns(_runs, sp, widthConstraint, baseTypeface, EffectiveLineHeightSp(), _lineBreak);
		}

		public override void Render(IComposer composer)
		{
			_ = _version.Value;

			var builder = new AnnotatedStringBuilder();
			foreach (var run in _runs)
			{
				var style = new SpanStyle();
				if (run.Color is { } c)
					style.Color = ToComposeColor(c);
				if (run.Monospace)
					style.FontFamily = FontFamily.Monospace;
				if (run.Background is { } bg)
					style.Background = ToComposeColor(bg);
				if (run.Bold)
					style.FontWeight = AndroidX.Compose.FontWeight.Bold;
				if (run.Italic)
					style.FontStyle = AndroidX.Compose.FontStyle.Italic;
				// Strikethrough and underline are mutually exclusive in practice; strike wins.
				if (run.Strikethrough)
					style.Decoration = TextDecoration.LineThrough;
				else if (run.Underline)
					style.Decoration = TextDecoration.Underline;
				if (run.FontSize is { } fsz)
					style.FontSize = new AndroidX.Compose.Sp((int)System.Math.Round(fsz));

				// A run with a tap handler becomes a clickable LinkAnnotation (the SpanStyle rides
				// along as the link's range style) so Compose's Text fires the click — mirrors the
				// gold's ClickableText @mention/link annotations. Otherwise a plain styled span.
				if (run.OnTap is { } onTap)
				{
					var link = LinkAnnotation.Clickable(string.Empty, _ => onTap(), style);
					int li = builder.PushLink(link);
					builder.Append(run.Text);
					builder.Pop(li);
				}
				else
				{
					int idx = builder.PushStyle(style);
					builder.Append(run.Text);
					builder.Pop(idx);
				}
			}

			var text = new ComposeAnnotatedText(builder.ToAnnotatedString()) { Modifier = BuildNodeModifier() };
			text.LetterSpacing = AndroidX.Compose.Sp.Zero;
			if (_fontSize > 0)
				text.FontSize = new AndroidX.Compose.Sp(_fontSize);
			text.LineHeight = new AndroidX.Compose.Sp(EffectiveLineHeightSp());
			// Base (non-code) runs use the body family; code runs override to monospace above.
			if (ComposeFontRegistry.Resolve(_fontFamily, 400) is { } r)
				text.FontFamily = r.Family;
			// Wrap strategy via a base TextStyle (see ComposeTextNode) — explicit params still win.
			if (_lineBreak != 0)
				text.Style = new AndroidX.Compose.TextStyle
				{
					LineBreak = _lineBreak == 1 ? LineBreakValues.Heading : LineBreakValues.Paragraph,
				}.Build();
			text.Render(composer);
		}
	}
}
#endif
