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
		Comet.ReturnType _returnType = Comet.ReturnType.Default;

		// TextFieldValue state (text + caret) for the borderless path: user edits hand back the
		// full value, and programmatic edits (insert-at-cursor) can place the caret.
		MutableState<AndroidX.Compose.UI.Text.Input.TextFieldValue>? _tfv;

		public ComposeTextFieldNode(Comet.TextField field)
			=> field.RegisterTextInserter(InsertAtCursor);

		/// <summary>A diff transferred this node to a new TextField instance: re-register the
		/// caret-aware inserter on it, or emoji-insert-at-cursor silently degrades to append
		/// (the new field's inserter would be null). Fires on ordinary re-renders and hot reload.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is Comet.TextField field)
				field.RegisterTextInserter(InsertAtCursor);
		}

		static long PackCaret(int start, int end) => ((long)start << 32) | (uint)end;

		MutableState<AndroidX.Compose.UI.Text.Input.TextFieldValue> Tfv
			=> _tfv ??= new(ComposeExtensions.NewTextFieldValue(
				_text.Value, PackCaret(_text.Value.Length, _text.Value.Length)));

		/// <summary>Inserts at the caret (replacing any selection), caret lands after the
		/// insert. The classic emoji-picker edit — mid-string, not append.</summary>
		void InsertAtCursor(string insert)
		{
			var current = Tfv.Value!;
			var text = current.Text ?? string.Empty;
			// Selection is the packed TextRange inline value: start in the high 32 bits, end low.
			// A TextRange can be reversed (start > end, a right-to-left drag), so normalize with
			// min/max before slicing — otherwise the selected span isn't replaced and the insert
			// lands at the wrong index.
			long sel = current.Selection;
			int a = (int)(sel >> 32);
			int b = (int)(sel & 0xFFFFFFFF);
			int start = System.Math.Clamp(System.Math.Min(a, b), 0, text.Length);
			int end = System.Math.Clamp(System.Math.Max(a, b), start, text.Length);
			var newText = text.Substring(0, start) + insert + text.Substring(end);
			int caret = start + insert.Length;
			Tfv.Value = ComposeExtensions.NewTextFieldValue(newText, PackCaret(caret, caret));
			_text.Value = newText;
			Sink?.OnEvent(EventIds.TextChanged, newText);
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.TextField_Text)
			{
				var s = value.AsString ?? string.Empty;
				_text.Value = s;
				// Programmatic text change (e.g. clear-on-send): rebuild the TextFieldValue with
				// the caret at the end. The user-typing echo arrives with IDENTICAL text — skip
				// it so the live caret position isn't reset mid-typing.
				if (_tfv is not null && (_tfv.Value?.Text ?? string.Empty) != s)
					_tfv.Value = ComposeExtensions.NewTextFieldValue(s, PackCaret(s.Length, s.Length));
			}
			else if (id == PropertyIds.TextField_Placeholder)
				_placeholder.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Borderless)
				_borderless.Value = value.AsBool;
			else if (id == PropertyIds.TextField_TextColor)
				_textColor = value.AsColor;
			else if (id == PropertyIds.TextField_ReturnType)
				_returnType = (Comet.ReturnType)value.AsInt;
		}

		// Map Comet's ReturnType (the soft-keyboard action key) to a Compose ImeAction int.
		static int MapImeAction(Comet.ReturnType rt) => rt switch
		{
			Comet.ReturnType.Send => AndroidX.Compose.ImeAction.Send,
			Comet.ReturnType.Done => AndroidX.Compose.ImeAction.Done,
			Comet.ReturnType.Go => AndroidX.Compose.ImeAction.Go,
			Comet.ReturnType.Next => AndroidX.Compose.ImeAction.Next,
			Comet.ReturnType.Search => AndroidX.Compose.ImeAction.Search,
			_ => AndroidX.Compose.ImeAction.Default,
		};

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

			var field = new AndroidX.Compose.BasicTextField(
				Tfv.Value!, tfv =>
				{
					Tfv.Value = tfv;
					var s = tfv.Text ?? string.Empty;
					if (_text.Value != s)
					{
						_text.Value = s;
						Sink?.OnEvent(EventIds.TextChanged, s);
					}
				})
			{
				// Report focus GAINED so the host can react (gold onTextFieldFocused — e.g. close an open
				// input-selector panel so the keyboard doesn't overlay it).
				Modifier = contentMod.OnFocusChanged(fs => { if (fs.IsFocused) Sink?.OnEvent(EventIds.Focused); }),
				SingleLine = true,
				TextStyle = new AndroidX.Compose.TextStyle { Color = textColor, FontSize = new AndroidX.Compose.Sp(BorderlessFontSp) },
			};

			// Soft-keyboard action key (e.g. Send): set the ImeAction and fire Completed when it's pressed
			// (the gold's KeyboardActions { onMessageSent }). All action callbacks route to Completed —
			// only the configured action's key is shown by the IME, so just one can fire.
			if (_returnType != Comet.ReturnType.Default)
			{
				// Copy the default options overriding only imeAction. The binding strips Kotlin defaults,
				// so pass all slots: capitalization None(0), autoCorrect default, keyboardType Text, the
				// mapped action; the trailing platformImeOptions / showKeyboardOnFocus / hintLocales = null.
				field.KeyboardOptions = AndroidX.Compose.KeyboardOptionsCompanion.Default.Copy(
					0, null, AndroidX.Compose.KeyboardType.Text, MapImeAction(_returnType), null, null, null);
				void Fire() => Sink?.OnEvent(EventIds.Completed);
				field.KeyboardActions = AndroidX.Compose.KeyboardActionsHelper.Create(
					onDone: Fire, onGo: Fire, onNext: Fire, onSearch: Fire, onSend: Fire);
			}

			// Field FIRST so it keeps a stable position (index 0) across text changes. The placeholder is
			// ALWAYS present (blanked once there's input) and overlaid on top — adding/removing it instead
			// would shift the field's index and make Compose drop focus + dismiss the keyboard mid-typing.
			box.Add(field);
			var hint = string.IsNullOrEmpty(_text.Value) ? (_placeholder.Value ?? string.Empty) : string.Empty;
			box.Add(new AndroidX.Compose.Text(hint)
			{
				Modifier = contentMod,
				FontSize = new AndroidX.Compose.Sp(BorderlessFontSp),
				// Dim the hint (≈60% alpha) — reads like onSurfaceVariant.
				Color = _textColor is { } c
					? ToComposeColor(new Microsoft.Maui.Graphics.Color(c.Red, c.Green, c.Blue, 0.6f))
					: AndroidX.Compose.Color.Gray,
			});

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
