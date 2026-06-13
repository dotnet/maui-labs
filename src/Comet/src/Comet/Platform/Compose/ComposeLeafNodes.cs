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
		}

		// Intrinsic size for the Yoga engine: measure the text wrapped to the available width
		// with StaticLayout (synchronous, no composition needed). 16sp ~ Material bodyLarge.
		public override Size Measure(double widthConstraint, double heightConstraint)
			=> TextMeasure.MeasureWrapped(_text.Value, 16f, widthConstraint);

		public override void Render(IComposer composer)
		{
			// Reading _text.Value inside composition subscribes this scope, so a later
			// ApplyProperty -> setValue recomposes just this Text.
			_ = _colorVersion.Value; // subscribe so a color change recomposes
			var text = new ComposeText(_text.Value) { Modifier = BuildNodeModifier() };
			if (_color is { } c)
				text.Color = ToComposeColor(c);
			text.Render(composer);
		}
	}

	/// <summary>Synchronous native text measurement for the Yoga layout engine.</summary>
	static class TextMeasure
	{
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
			return new Size(widthPx / density, layout.Height / density);
		}
	}

	/// <summary>Renders Comet <c>Button</c> as a Material 3 filled <c>Button</c>,
	/// routing clicks back through the event sink.</summary>
	sealed class ComposeButtonNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Button_Text)
				_text.Value = value.AsString ?? string.Empty;
		}

		// A Material filled button: label plus default content padding, min 48dp tall.
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			var label = TextMeasure.SingleLine(_text.Value, 14f);
			return new Size(label.Width + 48, System.Math.Max(label.Height + 20, 48));
		}

		public override void Render(IComposer composer)
		{
			var button = new ComposeButton(onClick: () => Sink?.OnEvent(EventIds.Clicked));
			// Apply the Yoga frame (offset+size) so the button is positioned like every other node.
			((ComposableNode)button).Modifier = BuildNodeModifier();
			button.Add(new ComposeText(_text.Value));
			button.Render(composer);
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
