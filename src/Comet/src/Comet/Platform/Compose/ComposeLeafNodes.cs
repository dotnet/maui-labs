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
		int _fontWeight;
		readonly MutableState<int> _colorVersion = new(0);

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
			else if (id == PropertyIds.Text_FontWeight)
			{
				_fontWeight = (int)value.AsDouble;
				_colorVersion.Value++;
			}
		}

		// Intrinsic size for the Yoga engine: measure the text wrapped to the available width
		// with StaticLayout (synchronous, no composition needed). 16sp ~ Material bodyLarge.
		public override Size Measure(double widthConstraint, double heightConstraint)
			=> TextMeasure.MeasureWrapped(_text.Value, _fontSize > 0 ? _fontSize : 16f, widthConstraint);

		public override void Render(IComposer composer)
		{
			// Reading _text.Value inside composition subscribes this scope, so a later
			// ApplyProperty -> setValue recomposes just this Text.
			_ = _colorVersion.Value; // subscribe so a color/size/weight change recomposes
			var text = new ComposeText(_text.Value) { Modifier = BuildNodeModifier() };
			// Zero letter-spacing so the rendered width matches the Paint measurement above (the
			// MaterialTheme default bodyLarge adds 0.5sp, which otherwise overflows the frame).
			text.LetterSpacing = AndroidX.Compose.Sp.Zero;
			if (_color is { } c)
				text.Color = ToComposeColor(c);
			if (_fontSize > 0)
				text.FontSize = new AndroidX.Compose.Sp(_fontSize);
			if (_fontWeight > 0)
				text.FontWeight = MapWeight(_fontWeight);
			// Pin the rendered line-height to exactly what the measurement used (instead of the
			// MaterialTheme bodyLarge default of 24sp) so multi-line text doesn't clip, a single
			// line isn't an over-tall box (which threw off vertical centering + the title/subtitle
			// gap), and the frame height matches the render line-for-line.
			text.LineHeight = new AndroidX.Compose.Sp(TextMeasure.LineHeightSp(_fontSize > 0 ? _fontSize : 16f));
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
		// the multi-line height (the Compose analog of iOS TextKit boundingRect).
		public static Size MeasureWrapped(string? text, float sp, double maxWidthDp)
		{
			var density = ComposeNode.Density;
			var s = text ?? string.Empty;
			int widthPx = (maxWidthDp > 0 && !double.IsInfinity(maxWidthDp))
				? (int)System.Math.Ceiling(maxWidthDp * density)
				: global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels;
			using var paint = new global::Android.Text.TextPaint { TextSize = sp * density };
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
			// Height: lineCount × the SAME line-height the renderer pins (LineHeightSp), so the frame
			// is exactly as tall as the composed Text — no clipping, no extra slack.
			double heightDp = layout.LineCount * LineHeightSp(sp);
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
			var view = new AndroidView(factory: ctx =>
			{
				var iv = new global::Android.Widget.ImageView(ctx);
				iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.CenterCrop);
				Load(iv, url);
				return iv;
			});
			((ComposableNode)view).Modifier = BuildNodeModifier();
			view.Render(composer);
		}

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
