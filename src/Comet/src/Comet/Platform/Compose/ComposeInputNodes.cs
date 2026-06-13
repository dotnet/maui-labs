#nullable enable
#if ANDROID
using System;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeTextField = AndroidX.Compose.TextField;
using ComposeSwitch = AndroidX.Compose.Switch;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <c>TextField</c> as a Material 3 <c>TextField</c>, routing
	/// edits back through the event sink (TextChanged payload).</summary>
	sealed class ComposeTextFieldNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);
		readonly MutableState<string> _placeholder = new(string.Empty);

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
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

	/// <summary>Renders Comet <c>Toggle</c> as a Material 3 <c>Switch</c>, routing flips
	/// back through the event sink (Toggled bool payload).</summary>
	sealed class ComposeToggleNode : ComposeNode
	{
		readonly MutableState<bool> _isOn = new(false);

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
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
