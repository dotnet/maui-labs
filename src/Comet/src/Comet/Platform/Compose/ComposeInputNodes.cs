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

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.TextField_Text)
				_text.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Placeholder)
				_placeholder.Value = value.AsString ?? string.Empty;
		}

		public override void Render(IComposer composer)
		{
			var field = new ComposeTextField(
				value: _text.Value,
				onValueChange: s => Sink?.OnEvent(EventIds.TextChanged, s));

			var placeholder = _placeholder.Value;
			if (!string.IsNullOrEmpty(placeholder))
				field.Placeholder = new AndroidX.Compose.Text(placeholder);

			field.Render(composer);
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
