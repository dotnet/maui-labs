#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
// Comet has its own Text/Button controls; alias the vendored Compose composables.
using ComposeText = AndroidX.Compose.Text;
using ComposeButton = AndroidX.Compose.Button;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <c>Text</c> as a Material 3 <c>Text</c> composable.</summary>
	sealed class ComposeTextNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);
		Microsoft.Maui.Graphics.Color? _color;
		int _fontSize;
		int _lineHeight;
		int _fontWeight;
		string? _fontFamily;
		readonly MutableState<int> _colorVersion = new(0);

		// The line height to pin (sp): an explicit .LineHeight() value (Compose TextStyle.lineHeight),
		// else the proportional heuristic. Used identically by render, measure, and baseline so the
		// frame, the drawn text, and the reported baseline all agree.
		int EffectiveLineHeightSp() => _lineHeight > 0 ? _lineHeight : TextMeasure.LineHeightSp(_fontSize > 0 ? _fontSize : 16f);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Value)
				_text.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.Text_Color)
			{
				_color = value.AsColor;
				_colorVersion.Value++;
			}
			else if (id == PropertyIds.Text_FontSize)
			{
				_fontSize = (int)System.Math.Round(value.AsDouble);
				_colorVersion.Value++;
			}
			else if (id == PropertyIds.Text_LineHeight)
			{
				_lineHeight = (int)System.Math.Round(value.AsDouble);
				_colorVersion.Value++;
			}
			else if (id == PropertyIds.Text_FontWeight)
			{
				_fontWeight = value.AsInt;   // emitted as From((int)weight) — read as Int, not Double
				_colorVersion.Value++;
			}
			else if (id == PropertyIds.Text_FontFamily)
			{
				_fontFamily = value.AsString;
				_colorVersion.Value++;
			}
		}

		// Intrinsic size for the Yoga engine: measure the text wrapped to the available width
		// with StaticLayout (synchronous, no composition needed). Uses the resolved custom typeface
		// so measurement matches the rendered (custom-font) glyphs. 16sp ~ Material bodyLarge.
		public override Size Measure(double widthConstraint, double heightConstraint)
			=> TextMeasure.MeasureWrapped(_text.Value, _fontSize > 0 ? _fontSize : 16f, widthConstraint, MeasureTypeface(), EffectiveLineHeightSp());

		// First-baseline offset (Dp) measured by Compose's OWN layout (TextMeasurer) so the reported
		// baseline equals the drawn one (the RN single-engine model). Only invoked for baseline-aligned
		// rows, and the list caches materialized rows so it runs once per row, not per scroll frame.
		public override double? MeasureBaseline(double width, double height)
		{
			int sp = _fontSize > 0 ? _fontSize : 16;
			var resolved = ComposeFontRegistry.Resolve(_fontFamily, _fontWeight);
			// Mirror Render: a resolved custom family carries the weight (no FontWeight); otherwise
			// apply the weight to the default family.
			AndroidX.Compose.FontFamily? family = resolved?.Family;
			AndroidX.Compose.FontWeight? weight = resolved is null && _fontWeight > 0 ? MapWeight(_fontWeight) : null;
			return AndroidX.Compose.ComposeTextMeasure.FirstBaselineDp(
				global::Android.App.Application.Context!, ComposeNode.Density, _text.Value,
				new AndroidX.Compose.Sp(sp), family, weight, new AndroidX.Compose.Sp(EffectiveLineHeightSp()), width);
		}

		// The typeface to measure with: the resolved custom font (which carries the weight), or the
		// default font at the requested weight — so bold default-font text isn't measured at Regular
		// width (which clips the heavier rendered glyphs).
		global::Android.Graphics.Typeface? MeasureTypeface()
		{
			if (ComposeFontRegistry.Resolve(_fontFamily, _fontWeight)?.Typeface is { } custom)
				return custom;
			if (_fontWeight >= 500 && System.OperatingSystem.IsAndroidVersionAtLeast(28))
				return global::Android.Graphics.Typeface.Create(global::Android.Graphics.Typeface.Default, _fontWeight, false);
			return null;
		}

		public override void Render(IComposer composer)
		{
			// Reading _text.Value inside composition subscribes this scope, so a later
			// ApplyProperty -> setValue recomposes just this Text.
			_ = _colorVersion.Value; // subscribe so a color/size/weight/family change recomposes
			var text = new ComposeText(_text.Value) { Modifier = BuildNodeModifier() };
			// Zero letter-spacing so the rendered width matches the Paint measurement above (the
			// MaterialTheme default bodyLarge adds 0.5sp, which otherwise overflows the frame).
			text.LetterSpacing = AndroidX.Compose.Sp.Zero;
			if (_color is { } c)
				text.Color = ToComposeColor(c);
			if (_fontSize > 0)
				text.FontSize = new AndroidX.Compose.Sp(_fontSize);
			// Custom font: the resolved typeface already carries the weight, so set the family and
			// DON'T also set FontWeight (which would synthesize bold on top). Falls back to the
			// weight when no custom family is registered.
			var resolved = ComposeFontRegistry.Resolve(_fontFamily, _fontWeight);
			if (resolved is { } r)
				text.FontFamily = r.Family;
			else if (_fontWeight > 0)
				text.FontWeight = MapWeight(_fontWeight);
			// Pin the rendered line-height to exactly what the measurement used (instead of the
			// MaterialTheme bodyLarge default of 24sp) so multi-line text doesn't clip, a single
			// line isn't an over-tall box (which threw off vertical centering + the title/subtitle
			// gap), and the frame height matches the render line-for-line.
			text.LineHeight = new AndroidX.Compose.Sp(EffectiveLineHeightSp());
			text.Render(composer);
		}

		static AndroidX.Compose.FontWeight MapWeight(int w) =>
			w >= 700 ? AndroidX.Compose.FontWeight.Bold
			: w >= 600 ? AndroidX.Compose.FontWeight.SemiBold
			: w >= 500 ? AndroidX.Compose.FontWeight.Medium
			: AndroidX.Compose.FontWeight.Normal;
	}

	/// <summary>Synchronous native text measurement for the Yoga layout engine.</summary>
	static class TextMeasure
	{
		// A comfortable line-height multiple (close to Roboto's natural ~1.17 plus a little), used
		// BOTH to set the rendered Text's lineHeight and to compute the measured frame height — so
		// measure == render and nothing clips. Integer sp so the two sides match exactly.
		const float LineMult = 1.3f;
		public static int LineHeightSp(float sp) => (int)System.Math.Round(sp * LineMult);

		// Measures formatted runs by building a native SpannableString with the same monospace
		// ("code") spans the renderer uses, so wrapping/height match the composed AnnotatedText.
		public static Size MeasureRuns(System.Collections.Generic.IReadOnlyList<Comet.TextRun> runs,
			float sp, double maxWidthDp, global::Android.Graphics.Typeface? baseTypeface, int lineHeightSp = 0)
		{
			var density = ComposeNode.Density;
			int widthPx = (maxWidthDp > 0 && !double.IsInfinity(maxWidthDp))
				? (int)System.Math.Ceiling(maxWidthDp * density)
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels;

			var sb = new global::Android.Text.SpannableStringBuilder();
			foreach (var run in runs)
			{
				int start = sb.Length();
				sb.Append(run.Text ?? string.Empty);
				if (run.Monospace)
					sb.SetSpan(new global::Android.Text.Style.TypefaceSpan("monospace"),
						start, sb.Length(), global::Android.Text.SpanTypes.InclusiveExclusive);
			}

			using var paint = new global::Android.Text.TextPaint { TextSize = sp * density };
			if (baseTypeface is not null)
				paint.SetTypeface(baseTypeface);
			using var layout = global::Android.Text.StaticLayout.Builder
				.Obtain(sb, 0, sb.Length(), paint, widthPx)
				.Build();

			float used = 0f;
			for (int i = 0; i < layout.LineCount; i++)
				used = System.Math.Max(used, layout.GetLineWidth(i));
			double heightDp = layout.LineCount * (lineHeightSp > 0 ? lineHeightSp : LineHeightSp(sp));
			return new Size((System.Math.Ceiling(used) + 2) / density, heightDp);
		}

		// First-baseline offset (Dp) from the top of a line whose height is pinned to LineHeightSp.
		// Compose distributes the extra leading (lineHeight − natural ascent/descent) PROPORTIONALLY
		// to the ascent:descent ratio (the default when no LineHeightStyle is set), so the baseline
		// sits at topLeading + ascent. Matching this exactly is what makes a baseline-aligned row
		// line up to the pixel instead of ~2px off.
		public static double FirstBaselineDp(float sp, global::Android.Graphics.Typeface? typeface)
		{
			var density = ComposeNode.Density;
			using var paint = new global::Android.Graphics.Paint { TextSize = sp * density };
			if (typeface is not null)
				paint.SetTypeface(typeface);
			var fm = paint.GetFontMetrics();
			float ascent = -fm.Ascent;                 // px above the baseline
			float descent = fm.Descent;                // px below the baseline
			float naturalPx = ascent + descent;
			float lineHeightPx = LineHeightSp(sp) * density;
			float extraPx = System.Math.Max(0f, lineHeightPx - naturalPx);
			float topLeadingPx = naturalPx > 0 ? extraPx * ascent / naturalPx : 0f;
			return (topLeadingPx + ascent) / density;
		}

		// Single-line width (e.g. a button label).
		public static Size SingleLine(string? text, float sp)
		{
			var density = ComposeNode.Density;
			using var paint = new global::Android.Graphics.Paint { TextSize = sp * density };
			float wPx = paint.MeasureText(text ?? string.Empty);
			var fm = paint.GetFontMetrics();
			return new Size(wPx / density, (fm.Descent - fm.Ascent) / density);
		}

		// Wrapped to the available width: StaticLayout lays the text out to widthPx and reports
		// the multi-line height (the Compose analog of iOS TextKit boundingRect). When a custom
		// typeface is supplied it is used so the measurement matches the rendered glyph metrics.
		public static Size MeasureWrapped(string? text, float sp, double maxWidthDp, global::Android.Graphics.Typeface? typeface = null, int lineHeightSp = 0)
		{
			var density = ComposeNode.Density;
			var s = text ?? string.Empty;
			int widthPx = (maxWidthDp > 0 && !double.IsInfinity(maxWidthDp))
				? (int)System.Math.Ceiling(maxWidthDp * density)
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels;
			using var paint = new global::Android.Text.TextPaint { TextSize = sp * density };
			if (typeface is not null)
				paint.SetTypeface(typeface);
			using var layout = global::Android.Text.StaticLayout.Builder
				.Obtain(s, 0, s.Length, paint, widthPx)
				.Build();

			// Report the ACTUAL used width (the widest laid-out line), not the constraint — so a
			// short label hugs its text (and the flex row packs it tight) while a long one still
			// wraps to widthPx. Returning the constraint width made every Text fill its column.
			float used = 0f;
			for (int i = 0; i < layout.LineCount; i++)
				used = System.Math.Max(used, layout.GetLineWidth(i));
			// Width: +2px absorbs hinting differences so a one-line label never clips.
			// Height: lineCount × the SAME line-height the renderer pins (explicit if supplied, else
			// the heuristic), so the frame is exactly as tall as the composed Text — no clipping.
			double heightDp = layout.LineCount * (lineHeightSp > 0 ? lineHeightSp : LineHeightSp(sp));
			return new Size((System.Math.Ceiling(used) + 2) / density, heightDp);
		}
	}

	/// <summary>Renders Comet <c>Button</c> as a Material 3 filled <c>Button</c>,
	/// routing clicks back through the event sink.</summary>
	sealed class ComposeButtonNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);
		Microsoft.Maui.Graphics.Color? _textColor;
		bool _outlined;
		readonly MutableState<int> _styleVersion = new(0);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Button_Text)
				_text.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.Button_TextColor)
			{
				_textColor = value.AsColor;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Button_Outlined)
			{
				_outlined = value.AsBool;
				_styleVersion.Value++;
			}
		}

		// A Material button: label plus default content padding, min 48dp tall.
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			var label = TextMeasure.SingleLine(_text.Value, 14f);
			return new Size(label.Width + 48, System.Math.Max(label.Height + 20, 48));
		}

		public override void Render(IComposer composer)
		{
			_ = _styleVersion.Value;

			// A real Material Button — filled by default, or OutlinedButton (bordered, no fill) when
			// asked. Content color + corner shape come from the Comet view's .Color()/.CornerRadius().
			var label = new ComposeText(_text.Value);
			void OnClick() => Sink?.OnEvent(EventIds.Clicked);

			if (_outlined)
			{
				var button = new AndroidX.Compose.OutlinedButton(OnClick);
				if (_textColor is { } tc)
					button.Colors = composer.ButtonColors(contentColor: (long)ToComposeColor(tc));
				if (HasRoundedCorners)
					button.Shape = CornerShape();
				((ComposableNode)button).Modifier = BuildNodeModifier();
				button.Add(label);
				button.Render(composer);
			}
			else
			{
				var button = new ComposeButton(OnClick);
				if (_textColor is { } tc)
					button.Colors = composer.ButtonColors(contentColor: (long)ToComposeColor(tc));
				if (HasRoundedCorners)
					button.Shape = CornerShape();
				((ComposableNode)button).Modifier = BuildNodeModifier();
				button.Add(label);
				button.Render(composer);
			}
		}
	}

	/// <summary>Renders Comet <c>Image</c> by hosting a native <c>ImageView</c> (via Compose
	/// AndroidView) and loading the URL source asynchronously. Images carry no intrinsic layout
	/// size, so callers give them an explicit Frame; the Yoga engine positions/sizes the node.</summary>
	sealed class ComposeImageNode : ComposeNode
	{
		static readonly System.Net.Http.HttpClient Http = new();
		readonly MutableState<string> _url = new(string.Empty);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Image_Source)
				_url.Value = value.AsString ?? string.Empty;
		}

		public override void Render(IComposer composer)
		{
			var url = _url.Value; // subscribe so a source change recomposes

			// A bundled drawable renders through the real Compose Image widget (painterResource),
			// cropped + clipped by the node's modifier — exactly the gold standard's
			// Image(painterResource(...), contentScale = Crop, modifier = …clip(CircleShape)).
			if (!string.IsNullOrEmpty(url) && !url.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
			{
				var ctx = global::Android.App.Application.Context;
				int resId = ctx.Resources!.GetIdentifier(url, "drawable", ctx.PackageName);
				if (resId != 0)
				{
					var image = new AndroidX.Compose.Image(resId)
					{
						ContentScale = AndroidX.Compose.ContentScale.Crop,
						Modifier = BuildNodeModifier(),
					};
					image.Render(composer);
					return;
				}
			}

			// Remote URL: there's no Bitmap→Painter bridge, so host a native ImageView and clip it
			// with a hardware outline (Compose's Modifier.clip doesn't reliably round an AndroidView).
			float radiusPx = CornerRadiusDp * ComposeNode.Density;
			bool rounded = HasRoundedCorners;
			var view = new AndroidView(factory: ctx =>
			{
				var iv = new global::Android.Widget.ImageView(ctx);
				iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.CenterCrop);
				if (rounded)
				{
					iv.OutlineProvider = new RoundedOutlineProvider(radiusPx);
					iv.ClipToOutline = true;
				}
				Load(iv, url);
				return iv;
			});
			((ComposableNode)view).Modifier = BuildNodeModifier();
			view.Render(composer);
		}

		// Clips a hosted ImageView to a circle (radius ≥ half the size) or a rounded rect, using the
		// view's own px bounds so the cropped bitmap fills a true circle with no width distortion.
		sealed class RoundedOutlineProvider : global::Android.Views.ViewOutlineProvider
		{
			readonly float _radiusPx;
			public RoundedOutlineProvider(float radiusPx) => _radiusPx = radiusPx;
			public override void GetOutline(global::Android.Views.View view, global::Android.Graphics.Outline outline)
			{
				int w = view.Width, h = view.Height;
				if (w <= 0 || h <= 0)
					return;
				if (_radiusPx * 2f >= System.Math.Min(w, h))
					outline.SetOval(0, 0, w, h);
				else
					outline.SetRoundRect(0, 0, w, h, _radiusPx);
			}
		}

		// Async network fetch for a remote (http) source; bundled drawables take the Compose Image
		// path in Render instead.
		static void Load(global::Android.Widget.ImageView iv, string url)
		{
			if (string.IsNullOrEmpty(url))
				return;
			_ = System.Threading.Tasks.Task.Run(async () =>
			{
				try
				{
					var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
					var bmp = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
					if (bmp is not null)
						iv.Post(() => iv.SetImageBitmap(bmp));
				}
				catch { /* leave the empty image view */ }
			});
		}
	}
}
#endif
