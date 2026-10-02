#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + input write-back for TextField.
	public partial class TextField
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			var text = Text?.CurrentValue;
			if (text is not null)
				node.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(text));

			var placeholder = Placeholder?.CurrentValue;
			if (placeholder is not null)
				node.ApplyProperty(PropertyIds.TextField_Placeholder, PropertyValue.From(placeholder));

			if (this.GetEnvironment<Microsoft.Maui.Graphics.Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.TextField_TextColor, PropertyValue.From(color));

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.Size) is { } fontSize)
				node.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(fontSize));

			if (this.GetEnvironment<string>(EnvironmentKeys.Fonts.Family) is { Length: > 0 } family)
			{
				var registration = FontFamilyRegistry.Resolve(family);
				node.ApplyProperty(PropertyIds.Text_FontFamily, PropertyValue.From(registration.FaceName));
				if (this.GetEnvironment<Microsoft.Maui.FontWeight?>(EnvironmentKeys.Fonts.Weight) is not { } &&
					registration.PreferredWeight is { } registeredWeight)
					node.ApplyProperty(PropertyIds.Text_FontWeight, PropertyValue.From((int)registeredWeight));
			}

			if (this.GetEnvironment<Microsoft.Maui.FontWeight?>(EnvironmentKeys.Fonts.Weight) is { } weight)
				node.ApplyProperty(PropertyIds.Text_FontWeight, PropertyValue.From((int)weight));

			if (this.GetEnvironment<Microsoft.Maui.FontSlant?>(EnvironmentKeys.Fonts.Slant) is
				Microsoft.Maui.FontSlant.Italic or Microsoft.Maui.FontSlant.Oblique)
			{
				node.ApplyProperty(PropertyIds.Text_Italic, PropertyValue.From(true));
			}

			if (this.GetEnvironment<bool?>(this, "Comet.TextFieldBorderless", false) == true)
				node.ApplyProperty(PropertyIds.TextField_Borderless, PropertyValue.From(true));

			if (this.GetEnvironment<bool?>(this, "Comet.TextFieldOutlined", false) == true)
				node.ApplyProperty(PropertyIds.TextField_Outlined, PropertyValue.From(true));

			if (this.GetEnvironment<string>(this, "Comet.TextFieldLeadingIcon", false) is { Length: > 0 } leading)
				node.ApplyProperty(PropertyIds.TextField_LeadingIcon, PropertyValue.From(leading));

			// Soft-keyboard "Send" action key (the gold composer). A dedicated bool flag set/read EXACTLY
			// like Borderless (cascades:false) — the generic ReturnType env (enum, cascades:true) doesn't
			// round-trip through the node-backend GetEnvironment read.
			if (this.GetEnvironment<bool?>(this, "Comet.TextFieldSendAction", false) == true)
				node.ApplyProperty(PropertyIds.TextField_ReturnType, PropertyValue.From((int)ReturnType.Send));

			if (this.GetEnvironment<Microsoft.Maui.Keyboard>(EnvironmentKeys.Entry.Keyboard, false) is { } keyboard)
			{
				var code = MapKeyboard(keyboard);
				if (code != 0)
					node.ApplyProperty(PropertyIds.TextField_Keyboard, PropertyValue.From(code));
			}
		}

		/// <summary>Maps a MAUI Keyboard instance to an int code for the backend property protocol.
		/// 0=Default, 1=Numeric, 2=Email, 3=Url, 4=Telephone, 5=Chat, 6=Plain.</summary>
		static int MapKeyboard(Microsoft.Maui.Keyboard keyboard)
		{
			if (keyboard == Microsoft.Maui.Keyboard.Numeric) return 1;
			if (keyboard == Microsoft.Maui.Keyboard.Email) return 2;
			if (keyboard == Microsoft.Maui.Keyboard.Url) return 3;
			if (keyboard == Microsoft.Maui.Keyboard.Telephone) return 4;
			if (keyboard == Microsoft.Maui.Keyboard.Chat) return 5;
			if (keyboard == Microsoft.Maui.Keyboard.Plain) return 6;
			return 0; // Default / Text
		}

		/// <summary>Makes the soft-keyboard action key a "Send" (ImeAction.Send) that fires
		/// <c>Completed</c> when pressed — the chat-composer submit-on-keyboard behavior.</summary>
		public TextField SendOnReturn()
		{
			this.SetEnvironment("Comet.TextFieldSendAction", true, false);
			return this;
		}

		/// <summary>Renders the REAL Material <c>OutlinedTextField</c> (the gold JetNews
		/// search field) — self-themed outline + label metrics.</summary>
		public TextField Outlined()
		{
			this.SetEnvironment("Comet.TextFieldOutlined", true, false);
			return this;
		}

		/// <summary>A leading icon inside the field, from the cross-platform symbol set
		/// (e.g. "search" — the gold search field's magnifier).</summary>
		public TextField LeadingIcon(string symbol)
		{
			this.SetEnvironment("Comet.TextFieldLeadingIcon", (object)symbol, false);
			return this;
		}

		/// <summary>Renders this field with no Material container or indicator line (a foundation
		/// <c>BasicTextField</c>) so it blends into its surroundings — e.g. a chat composer.</summary>
		public TextField Borderless()
		{
			this.SetEnvironment("Comet.TextFieldBorderless", true, false);
			return this;
		}

		protected internal override void OnBackendEvent<T>(Backend.EventId id, T payload)
		{
			// User edited the field. Optimistically reflect the new text on this control's own
			// node (the TextField is a controlled component), then write back through the
			// (possibly two-way) Text subscription so a bound Signal updates and dependents
			// re-render. See Toggle.OnBackendEvent for why the optimistic step is required.
			if (id == Backend.EventIds.TextChanged && payload is string s)
			{
				Node?.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(s));
				Text?.Set(s);
			}
		}

		System.Action<string>? _textInserter;

		/// <summary>Registered by a backend node that tracks the caret (selection): receives
		/// programmatic insert requests and applies them at the current cursor.</summary>
		internal void RegisterTextInserter(System.Action<string> inserter) => _textInserter = inserter;

		/// <summary>Inserts <paramref name="text"/> at the caret (replacing any selection) and
		/// places the caret after it — e.g. an emoji picker inserting mid-string. Appends when
		/// the backend doesn't track a caret.</summary>
		public void InsertAtCursor(string text)
		{
			if (string.IsNullOrEmpty(text))
				return;
			if (_textInserter is not null)
			{
				_textInserter(text);
				return;
			}
			var appended = (Text?.CurrentValue ?? string.Empty) + text;
			Node?.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(appended));
			Text?.Set(appended);
		}

		System.Action? _onFocused;

		/// <summary>Runs when the field gains focus (the gold's <c>onTextFieldFocused</c>) — e.g. close an
		/// open input-selector panel so the soft keyboard doesn't overlay it.</summary>
		public TextField OnFocused(System.Action onFocused)
		{
			_onFocused = onFocused;
			return this;
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// The soft-keyboard action key was pressed (e.g. Send) — fire the field's Completed handler.
			if (id == Backend.EventIds.Completed)
				Completed?.Invoke();
			else if (id == Backend.EventIds.Focused)
				_onFocused?.Invoke();
		}
	}
}
