using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class EntryHandler : GtkViewHandler<IEntry, Gtk.Entry>
{
	public static new IPropertyMapper<IEntry, EntryHandler> Mapper =
		new PropertyMapper<IEntry, EntryHandler>(ViewMapper)
		{
			[nameof(IEntry.Text)] = MapText,
			[nameof(IEntry.Placeholder)] = MapPlaceholder,
			[nameof(IEntry.IsPassword)] = MapIsPassword,
			[nameof(IEntry.MaxLength)] = MapMaxLength,
			[nameof(IEntry.IsReadOnly)] = MapIsReadOnly,
			[nameof(IEntry.TextColor)] = MapTextColor,
			[nameof(IEntry.Font)] = MapFont,
			[nameof(IEntry.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(IEntry.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IEntry.ClearButtonVisibility)] = MapClearButtonVisibility,
			[nameof(IEntry.CursorPosition)] = MapCursorPosition,
			[nameof(IEntry.SelectionLength)] = MapSelectionLength,
			[nameof(IEntry.ReturnType)] = MapReturnType,
			[nameof(IEntry.IsSpellCheckEnabled)] = MapIsSpellCheckEnabled,
			[nameof(IEntry.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
			[nameof(IEntry.Keyboard)] = MapKeyboard,
			[nameof(ITextAlignment.VerticalTextAlignment)] = MapVerticalTextAlignment,
		};

	public EntryHandler() : base(Mapper)
	{
	}

	protected override Gtk.Entry CreatePlatformView()
	{
		return Gtk.Entry.New();
	}

	protected override void ConnectHandler(Gtk.Entry platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnChanged += OnTextChanged;
	}

	protected override void DisconnectHandler(Gtk.Entry platformView)
	{
		platformView.OnChanged -= OnTextChanged;
		base.DisconnectHandler(platformView);
	}

	void OnTextChanged(Gtk.Editable sender, EventArgs args)
	{
		if (VirtualView != null)
			VirtualView.Text = sender.GetText();
	}

	public static void MapText(EntryHandler handler, IEntry entry)
	{
		if (handler.PlatformView?.GetText() != entry.Text)
			handler.PlatformView?.SetText(entry.Text ?? string.Empty);
	}

	public static void MapPlaceholder(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetPlaceholderText(entry.Placeholder ?? string.Empty);
	}

	public static void MapIsPassword(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetVisibility(!entry.IsPassword);
	}

	public static void MapMaxLength(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetMaxLength(entry.MaxLength);
	}

	public static void MapIsReadOnly(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetEditable(!entry.IsReadOnly);
	}

	public static void MapTextColor(EntryHandler handler, IEntry entry)
	{
		if (entry.TextColor != null)
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(entry.TextColor)};");
	}

	public static void MapFont(EntryHandler handler, IEntry entry)
	{
		var css = handler.BuildFontCss(entry.Font);
		if (!string.IsNullOrEmpty(css)) handler.ApplyCss(handler.PlatformView, css);
	}

	public static void MapHorizontalTextAlignment(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetAlignment(entry.HorizontalTextAlignment switch
		{
			TextAlignment.Start => 0f,
			TextAlignment.Center => 0.5f,
			TextAlignment.End => 1f,
			_ => 0f
		});
	}

	public static void MapCharacterSpacing(EntryHandler handler, IEntry entry)
	{
		handler.ApplyCss(handler.PlatformView, $"letter-spacing: {entry.CharacterSpacing}px;");
	}

	public static void MapClearButtonVisibility(EntryHandler handler, IEntry entry)
	{
		if (handler.PlatformView == null) return;

		if (entry.ClearButtonVisibility == ClearButtonVisibility.WhileEditing)
			handler.PlatformView.SetIconFromIconName(Gtk.EntryIconPosition.Secondary, "edit-clear-symbolic");
		else
			handler.PlatformView.SetIconFromIconName(Gtk.EntryIconPosition.Secondary, null);
	}

	public static void MapCursorPosition(EntryHandler handler, IEntry entry)
	{
		if (handler.PlatformView == null) return;
		var pos = Math.Clamp(entry.CursorPosition, 0, entry.Text?.Length ?? 0);
		handler.PlatformView.SetPosition(pos);
	}

	public static void MapSelectionLength(EntryHandler handler, IEntry entry)
	{
		if (handler.PlatformView == null) return;
		var start = entry.CursorPosition;
		var end = start + entry.SelectionLength;
		var textLen = entry.Text?.Length ?? 0;
		handler.PlatformView.SelectRegion(Math.Clamp(start, 0, textLen), Math.Clamp(end, 0, textLen));
	}

	public static void MapReturnType(EntryHandler handler, IEntry entry)
	{
		// ReturnType is a mobile IME concept; no direct GTK equivalent.
	}

	public static void MapIsSpellCheckEnabled(EntryHandler handler, IEntry entry)
	{
		// GTK Entry does not have built-in spell-check; intentional no-op.
	}

	public static void MapIsTextPredictionEnabled(EntryHandler handler, IEntry entry)
	{
		// GTK Entry does not support text prediction; intentional no-op.
	}

	public static void MapKeyboard(EntryHandler handler, IEntry entry)
	{
		handler.PlatformView?.SetInputPurpose(MapKeyboardToInputPurpose(entry.Keyboard));
	}

	public static void MapVerticalTextAlignment(EntryHandler handler, IEntry entry)
	{
		if (handler.PlatformView == null || entry is not ITextAlignment ta) return;
		handler.PlatformView.SetValign(ta.VerticalTextAlignment switch
		{
			TextAlignment.Start => Gtk.Align.Start,
			TextAlignment.Center => Gtk.Align.Center,
			TextAlignment.End => Gtk.Align.End,
			_ => Gtk.Align.Center
		});
	}

	internal static Gtk.InputPurpose MapKeyboardToInputPurpose(Keyboard? keyboard)
	{
		if (keyboard == Keyboard.Numeric || keyboard == Keyboard.Telephone)
			return Gtk.InputPurpose.Digits;
		if (keyboard == Keyboard.Email)
			return Gtk.InputPurpose.Email;
		if (keyboard == Keyboard.Url)
			return Gtk.InputPurpose.Url;
		return Gtk.InputPurpose.FreeForm;
	}
}
