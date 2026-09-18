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
		int _keyboardTypeCode; // 0=Default 1=Numeric 2=Email 3=Url 4=Telephone 5=Chat 6=Plain
		int _fontSize;
		int _fontWeight;
		string? _fontFamily;
		bool _fontItalic;
		AndroidX.Compose.FocusRequester? _focusRequester;
		readonly MutableState<int> _styleVersion = new(0);

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
			else if (id == PropertyIds.TextField_Outlined)
				_outlined.Value = value.AsBool;
			else if (id == PropertyIds.TextField_LeadingIcon)
				_leadingIcon.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Borderless)
				_borderless.Value = value.AsBool;
			else if (id == PropertyIds.TextField_TextColor)
				_textColor = value.AsColor;
			else if (id == PropertyIds.Text_FontSize)
			{
				_fontSize = (int)System.Math.Round(value.AsDouble);
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Text_FontFamily)
			{
				_fontFamily = value.AsString;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Text_FontWeight)
			{
				_fontWeight = value.AsInt;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Text_Italic)
			{
				_fontItalic = value.AsBool;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.TextField_ReturnType)
				_returnType = (Comet.ReturnType)value.AsInt;
			else if (id == PropertyIds.TextField_FocusRequested && value.AsBool)
				_focusRequester?.RequestFocus();
			else if (id == PropertyIds.TextField_Keyboard)
				_keyboardTypeCode = value.AsInt;
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

		/// <summary>Maps the protocol keyboard code to a Compose KeyboardType constant.
		/// Numeric maps to Decimal (allows decimal separator for values like 6.5).</summary>
		int MapKeyboardType() => _keyboardTypeCode switch
		{
			1 => AndroidX.Compose.KeyboardType.Decimal,  // Numeric — allows decimal
			2 => AndroidX.Compose.KeyboardType.Email,
			3 => AndroidX.Compose.KeyboardType.Uri,
			4 => AndroidX.Compose.KeyboardType.Phone,
			6 => AndroidX.Compose.KeyboardType.Ascii,    // Plain
			_ => AndroidX.Compose.KeyboardType.Text,     // Default, Text, Chat
		};

		// A Material TextField fills the available width and is ~56dp tall; give Yoga that intrinsic
		// size so it doesn't collapse to zero height. A borderless field is sized to its text plus
		// the requested vertical padding (no Material container chrome).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double width = double.IsInfinity(widthConstraint) ? 0 : widthConstraint;
			var text = TextMeasure.SingleLine(
				string.IsNullOrEmpty(_text.Value) ? _placeholder.Value : _text.Value,
				EffectiveFontSize(),
				MeasureTypeface());
			if (_borderless.Value)
				return new Size(width, System.Math.Max(22, text.Height));
			return new Size(width, System.Math.Max(56, text.Height + 32));
		}

		readonly MutableState<bool> _outlined = new(false);
		readonly MutableState<string> _leadingIcon = new(string.Empty);

		public override void Render(IComposer composer)
		{
			_ = _styleVersion.Value;
			// Lazily create the FocusRequester — it's attached to the Modifier chain so
			// RequestFocus() programmatically opens the keyboard on this exact field.
			_focusRequester ??= new AndroidX.Compose.FocusRequester();

			if (_borderless.Value)
			{
				RenderBorderless(composer);
				return;
			}

			var placeholder = _placeholder.Value;

			// The REAL Material OutlinedTextField (gold HomeSearch) — self-themed outline,
			// optional leading icon from the shared cross-platform symbol set.
			if (_outlined.Value)
			{
				var outlined = new AndroidX.Compose.OutlinedTextField(
					value: _text.Value,
					onValueChange: s => Sink?.OnEvent(EventIds.TextChanged, s));
				if (!string.IsNullOrEmpty(placeholder))
					outlined.Placeholder = StyledPlaceholder(placeholder);
				if (_leadingIcon.Value is { Length: > 0 } leading)
					outlined.LeadingIcon = new AndroidX.Compose.Icon(ComposeIconNode.ResolveSymbol(leading), leading);
				if (_keyboardTypeCode != 0)
					outlined.KeyboardOptions = AndroidX.Compose.KeyboardOptionsCompanion.Default.Copy(
						0, null, MapKeyboardType(), AndroidX.Compose.ImeAction.Default, null, null, null);
				outlined.TextStyle = InputTextStyle(
					_textColor is { } outlinedColor ? ToComposeColor(outlinedColor) : AndroidX.Compose.Color.Black);
				((ComposableNode)outlined).Modifier = (BuildNodeModifier() ?? Modifier.Companion)
					.FocusRequester(_focusRequester!);
				outlined.Render(composer);
				return;
			}

			var field = new ComposeTextField(
				value: _text.Value,
				onValueChange: s => Sink?.OnEvent(EventIds.TextChanged, s));

			if (!string.IsNullOrEmpty(placeholder))
				field.Placeholder = StyledPlaceholder(placeholder);
			if (_keyboardTypeCode != 0)
				field.KeyboardOptions = AndroidX.Compose.KeyboardOptionsCompanion.Default.Copy(
					0, null, MapKeyboardType(), AndroidX.Compose.ImeAction.Default, null, null, null);
			field.TextStyle = InputTextStyle(
				_textColor is { } fieldColor ? ToComposeColor(fieldColor) : AndroidX.Compose.Color.Black);

			// Position + size the field from its Yoga frame so it doesn't render at the origin.
			((ComposableNode)field).Modifier = (BuildNodeModifier() ?? Modifier.Companion)
				.FocusRequester(_focusRequester!);
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
				Modifier = contentMod
					.FocusRequester(_focusRequester!)
					.OnFocusChanged(fs => { if (fs.IsFocused) Sink?.OnEvent(EventIds.Focused); }),
				SingleLine = true,
				TextStyle = InputTextStyle(textColor),
			};

			// Soft-keyboard action key (e.g. Send): set the ImeAction and fire Completed when it's pressed
			// (the gold's KeyboardActions { onMessageSent }). All action callbacks route to Completed —
			// only the configured action's key is shown by the IME, so just one can fire.
			if (_returnType != Comet.ReturnType.Default || _keyboardTypeCode != 0)
			{
				var kbType = _keyboardTypeCode != 0 ? MapKeyboardType() : AndroidX.Compose.KeyboardType.Text;
				var imeAction = _returnType != Comet.ReturnType.Default ? MapImeAction(_returnType) : AndroidX.Compose.ImeAction.Default;
				field.KeyboardOptions = AndroidX.Compose.KeyboardOptionsCompanion.Default.Copy(
					0, null, kbType, imeAction, null, null, null);
				if (_returnType != Comet.ReturnType.Default)
				{
					void Fire() => Sink?.OnEvent(EventIds.Completed);
					field.KeyboardActions = AndroidX.Compose.KeyboardActionsHelper.Create(
						onDone: Fire, onGo: Fire, onNext: Fire, onSearch: Fire, onSend: Fire);
				}
			}

			// Field FIRST so it keeps a stable position (index 0) across text changes. The placeholder is
			// ALWAYS present (blanked once there's input) and overlaid on top — adding/removing it instead
			// would shift the field's index and make Compose drop focus + dismiss the keyboard mid-typing.
			box.Add(field);
			var hint = string.IsNullOrEmpty(_text.Value) ? (_placeholder.Value ?? string.Empty) : string.Empty;
			var placeholder = new AndroidX.Compose.Text(hint)
			{
				Modifier = contentMod,
				FontSize = new AndroidX.Compose.Sp(EffectiveFontSize()),
				// Dim the hint (≈60% alpha) — reads like onSurfaceVariant.
				Color = _textColor is { } c
					? ToComposeColor(new Microsoft.Maui.Graphics.Color(c.Red, c.Green, c.Blue, 0.6f))
					: AndroidX.Compose.Color.Gray,
			};
			ApplyFont(placeholder);
			box.Add(placeholder);

			((ComposableNode)box).Render(composer);
		}

		int EffectiveFontSize() => _fontSize > 0 ? _fontSize : 16;

		AndroidX.Compose.TextStyle InputTextStyle(AndroidX.Compose.Color color)
		{
			var style = new AndroidX.Compose.TextStyle
			{
				Color = color,
				FontSize = new AndroidX.Compose.Sp(EffectiveFontSize()),
			};
			if (ComposeFontRegistry.Resolve(_fontFamily, _fontWeight, _fontItalic) is { } resolved)
				style.FontFamily = resolved.Family;
			else if (_fontWeight > 0)
				style.FontWeight = MapWeight(_fontWeight);
			if (_fontItalic)
				style.FontStyle = AndroidX.Compose.FontStyle.Italic;
			return style;
		}

		void ApplyFont(AndroidX.Compose.Text text)
		{
			if (ComposeFontRegistry.Resolve(_fontFamily, _fontWeight, _fontItalic) is { } resolved)
				text.FontFamily = resolved.Family;
			else if (_fontWeight > 0)
				text.FontWeight = MapWeight(_fontWeight);
			if (_fontItalic)
				text.FontStyle = AndroidX.Compose.FontStyle.Italic;
		}

		AndroidX.Compose.Text StyledPlaceholder(string text)
		{
			var placeholder = new AndroidX.Compose.Text(text)
			{
				FontSize = new AndroidX.Compose.Sp(EffectiveFontSize()),
			};
			ApplyFont(placeholder);
			return placeholder;
		}

		global::Android.Graphics.Typeface? MeasureTypeface()
		{
			if (ComposeFontRegistry.Resolve(_fontFamily, _fontWeight, _fontItalic)?.Typeface is { } custom)
				return custom;
			if ((_fontWeight != 0 || _fontItalic) && System.OperatingSystem.IsAndroidVersionAtLeast(28))
			{
				return global::Android.Graphics.Typeface.Create(
					global::Android.Graphics.Typeface.Default,
					_fontWeight > 0 ? _fontWeight : 400,
					_fontItalic);
			}
			return null;
		}

		static AndroidX.Compose.FontWeight MapWeight(int weight) =>
			weight >= 900 ? AndroidX.Compose.FontWeight.Black
			: weight >= 800 ? AndroidX.Compose.FontWeight.ExtraBold
			: weight >= 700 ? AndroidX.Compose.FontWeight.Bold
			: weight >= 600 ? AndroidX.Compose.FontWeight.SemiBold
			: weight >= 500 ? AndroidX.Compose.FontWeight.Medium
			: weight >= 400 ? AndroidX.Compose.FontWeight.Normal
			: weight >= 300 ? AndroidX.Compose.FontWeight.Light
			: weight >= 200 ? AndroidX.Compose.FontWeight.ExtraLight
			: AndroidX.Compose.FontWeight.Thin;
	}

	/// <summary>Renders Comet <c>Slider</c> as a Material 3 <c>Slider</c> (default 0..1
	/// range), routing drags back through the event sink (ValueChanged double payload).</summary>
	sealed class ComposeSliderNode : ComposeNode
	{
		readonly MutableState<float> _value = new(0f);
		readonly MutableState<float> _min = new(0f);
		readonly MutableState<float> _max = new(1f);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Slider_Value)
				_value.Value = (float)value.AsDouble;
			else if (id == PropertyIds.Slider_Minimum)
				_min.Value = (float)value.AsDouble;
			else if (id == PropertyIds.Slider_Maximum)
				_max.Value = (float)value.AsDouble;
		}

		public override void Render(IComposer composer)
		{
			var slider = new ComposeSlider(
				value: _value.Value,
				onValueChange: v => Sink?.OnEvent(EventIds.ValueChanged, (double)v));
			var minimum = _min.Value;
			var maximum = _max.Value;
			if (minimum != 0f || maximum != 1f)
				slider.ValueRange = Kotlin.Ranges.RangesKt.RangeTo(minimum, maximum);
			slider.Render(composer);
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
