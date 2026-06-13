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

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Text_Value)
				_text.Value = value.AsString ?? string.Empty;
		}

		// Intrinsic size for the Yoga engine: measure the text with the platform Paint
		// (synchronous, no composition needed). 16sp ~ Material bodyLarge.
		public override Size Measure(double widthConstraint, double heightConstraint)
			=> TextMeasure.Measure(_text.Value, 16f);

		public override void Render(IComposer composer)
		{
			// Reading _text.Value inside composition subscribes this scope, so a later
			// ApplyProperty -> setValue recomposes just this Text.
			new ComposeText(_text.Value) { Modifier = BuildNodeModifier() }.Render(composer);
		}
	}

	/// <summary>Synchronous native text measurement for the Yoga layout engine.</summary>
	static class TextMeasure
	{
		public static Size Measure(string? text, float sp)
		{
			var density = ComposeNode.Density;
			using var paint = new global::Android.Graphics.Paint { TextSize = sp * density };
			float wPx = paint.MeasureText(text ?? string.Empty);
			var fm = paint.GetFontMetrics();
			float hPx = fm.Descent - fm.Ascent;
			return new Size(wPx / density, hPx / density);
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
			var label = TextMeasure.Measure(_text.Value, 14f);
			return new Size(label.Width + 48, System.Math.Max(label.Height + 20, 48));
		}

		public override void Render(IComposer composer)
		{
			var button = new ComposeButton(onClick: () => Sink?.OnEvent(EventIds.Clicked));
			button.Add(new ComposeText(_text.Value));
			button.Render(composer);
		}
	}
}
#endif
