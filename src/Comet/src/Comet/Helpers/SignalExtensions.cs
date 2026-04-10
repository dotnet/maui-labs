using System;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>
	/// Convenience factory methods that create controls wired to <see cref="Signal{T}"/>
	/// via <see cref="PropertySubscription{T}"/>. These use the unified reactive system
	/// for change tracking.
	/// </summary>
	public static class SignalExtensions
	{
		/// <summary>
		/// Creates a <see cref="TextField"/> with bidirectional binding to a <see cref="Signal{T}"/>
		/// via <see cref="PropertySubscription{T}"/>. When the signal changes, the handler updates;
		/// when the user types, the signal receives the new value through WriteBack.
		/// </summary>
		public static TextField TextField(Signal<string> text, string placeholder = null, Action completed = null)
		{
			var field = completed is not null
				? new TextField(text, placeholder ?? "", completed)
				: placeholder is not null
					? new TextField(text, placeholder)
					: new TextField(text);

			var sub = new PropertySubscription<string>(text);
			field.AttachPropertySubscription(sub, nameof(IText.Text));
			return field;
		}

		/// <summary>
		/// Creates a <see cref="Slider"/> with bidirectional binding to a <see cref="Signal{T}"/>
		/// via <see cref="PropertySubscription{T}"/>.
		/// </summary>
		public static Slider Slider(Signal<double> value, double minimum = 0, double maximum = 1)
		{
			var slider = new Slider(value, minimum, maximum);

			var sub = new PropertySubscription<double>(value);
			slider.AttachPropertySubscription(sub, nameof(IRange.Value));
			return slider;
		}

		/// <summary>
		/// Creates a <see cref="TextEditor"/> with bidirectional binding to a <see cref="Signal{T}"/>
		/// via <see cref="PropertySubscription{T}"/>. When the signal changes, the editor updates;
		/// when the user types, the signal receives the new value through WriteBack.
		/// </summary>
		public static TextEditor TextEditor(Signal<string> text)
		{
			var editor = new TextEditor(text);

			var sub = new PropertySubscription<string>(text);
			editor.AttachPropertySubscription(sub, nameof(IEditor.Text));
			return editor;
		}

		/// <summary>
		/// Creates a <see cref="Toggle"/> with bidirectional binding to a <see cref="Signal{T}"/>
		/// via <see cref="PropertySubscription{T}"/>.
		/// </summary>
		public static Toggle Toggle(Signal<bool> isOn)
		{
			var toggle = new Toggle(isOn);

			var sub = new PropertySubscription<bool>(isOn);
			toggle.AttachPropertySubscription(sub, "Value");
			return toggle;
		}
	}
}
