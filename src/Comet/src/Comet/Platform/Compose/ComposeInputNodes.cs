#nullable enable
#if ANDROID
using System;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeTextField = AndroidX.Compose.TextField;
using ComposeSwitch = AndroidX.Compose.Switch;
using ComposeSlider = AndroidX.Compose.Slider;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <c>TextField</c> as a Material 3 <c>TextField</c>, routing
	/// edits back through the event sink (TextChanged payload).</summary>
	sealed class ComposeTextFieldNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);
		readonly MutableState<string> _placeholder = new(string.Empty);
		readonly MutableState<bool> _borderless = new(false);
		Microsoft.Maui.Graphics.Color? _textColor;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.TextField_Text)
				_text.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Placeholder)
				_placeholder.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Borderless)
				_borderless.Value = value.AsBool;
			else if (id == PropertyIds.TextField_TextColor)
				_textColor = value.AsColor;
		}

		// A Material TextField fills the available width and is ~56dp tall; give Yoga that intrinsic
		// size so it doesn't collapse to zero height. A borderless field is sized to its text plus
		// the requested vertical padding (no Material container chrome).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double width = double.IsInfinity(widthConstraint) ? 0 : widthConstraint;
			if (_borderless.Value)
				return new Size(width, 22 + Padding.Top + Padding.Bottom);
			return new Size(width, 56);
		}

		const int BorderlessFontSp = 16;

		public override void Render(IComposer composer)
		{
			if (_borderless.Value)
			{
				RenderBorderless(composer);
				return;
			}

			var field = new ComposeTextField(
				value: _text.Value,
				onValueChange: s => Sink?.OnEvent(EventIds.TextChanged, s));

			var placeholder = _placeholder.Value;
			if (!string.IsNullOrEmpty(placeholder))
				field.Placeholder = new AndroidX.Compose.Text(placeholder);

			// Position + size the field from its Yoga frame so it doesn't render at the origin.
			((ComposableNode)field).Modifier = BuildNodeModifier();
			field.Render(composer);
		}

		// A foundation BasicTextField (no container/indicator) overlaid with the placeholder when
		// empty — blends into the surrounding surface. Content padding is applied here because the
		// Yoga engine doesn't inset leaf content.
		void RenderBorderless(IComposer composer)
		{
			var p = Padding;
			var contentMod = Modifier.Companion
				.FillMaxWidth()
				.Padding(new Dp((float)p.Left), new Dp((float)p.Top), new Dp((float)p.Right), new Dp((float)p.Bottom));

			var textColor = _textColor is { } tc ? ToComposeColor(tc) : AndroidX.Compose.Color.Black;

			var box = new AndroidX.Compose.Box();
			((ComposableNode)box).Modifier = BuildNodeModifier();   // Yoga frame (+ any background)

			// Placeholder shows only while empty (this re-runs when _text changes, so it hides on type).
			var placeholder = _placeholder.Value;
			if (string.IsNullOrEmpty(_text.Value) && !string.IsNullOrEmpty(placeholder))
			{
				var ph = new AndroidX.Compose.Text(placeholder)
				{
					Modifier = contentMod,
					FontSize = new AndroidX.Compose.Sp(BorderlessFontSp),
					// Dim the text color for the hint (≈60% alpha) — reads like onSurfaceVariant.
					Color = _textColor is { } c
						? ToComposeColor(new Microsoft.Maui.Graphics.Color(c.Red, c.Green, c.Blue, 0.6f))
						: AndroidX.Compose.Color.Gray,
				};
				box.Add(ph);
			}

			var field = new AndroidX.Compose.BasicTextField(
				_text.Value, s => Sink?.OnEvent(EventIds.TextChanged, s))
			{
				Modifier = contentMod,
				SingleLine = true,
				TextStyle = new AndroidX.Compose.TextStyle { Color = textColor, FontSize = new AndroidX.Compose.Sp(BorderlessFontSp) },
			};
			box.Add(field);

			((ComposableNode)box).Render(composer);
		}
	}

	/// <summary>Renders Comet <c>Slider</c> as a Material 3 <c>Slider</c> (default 0..1
	/// range), routing drags back through the event sink (ValueChanged double payload).</summary>
	sealed class ComposeSliderNode : ComposeNode
	{
		readonly MutableState<float> _value = new(0f);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Slider_Value)
				_value.Value = (float)value.AsDouble;
		}

		public override void Render(IComposer composer)
		{
			new ComposeSlider(
				value: _value.Value,
				onValueChange: v => Sink?.OnEvent(EventIds.ValueChanged, (double)v))
				.Render(composer);
		}
	}

	/// <summary>Renders Comet <c>Toggle</c> as a Material 3 <c>Switch</c>, routing flips
	/// back through the event sink (Toggled bool payload).</summary>
	sealed class ComposeToggleNode : ComposeNode
	{
		readonly MutableState<bool> _isOn = new(false);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Toggle_IsOn)
				_isOn.Value = value.AsBool;
		}

		public override void Render(IComposer composer)
		{
			new ComposeSwitch(
				@checked: _isOn.Value,
				onCheckedChange: b => Sink?.OnEvent(EventIds.Toggled, b))
				.Render(composer);
		}
	}
}
#endif
