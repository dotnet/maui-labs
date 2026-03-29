using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class EditorHandler : GtkViewHandler<IEditor, Gtk.TextView>
{
	int? _maxLength;

	public static new IPropertyMapper<IEditor, EditorHandler> Mapper =
		new PropertyMapper<IEditor, EditorHandler>(ViewMapper)
		{
			[nameof(IEditor.Text)] = MapText,
			[nameof(IEditor.IsReadOnly)] = MapIsReadOnly,
			[nameof(IEditor.TextColor)] = MapTextColor,
			[nameof(IEditor.Font)] = MapFont,
			[nameof(IEditor.Placeholder)] = MapPlaceholder,
			[nameof(IEditor.PlaceholderColor)] = MapPlaceholderColor,
			[nameof(IEditor.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IEditor.CursorPosition)] = MapCursorPosition,
			[nameof(IEditor.SelectionLength)] = MapSelectionLength,
			[nameof(IEditor.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(IEditor.MaxLength)] = MapMaxLength,
			[nameof(IEditor.IsSpellCheckEnabled)] = MapIsSpellCheckEnabled,
			[nameof(IEditor.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
			[nameof(IEditor.Keyboard)] = MapKeyboard,
			[nameof(ITextAlignment.VerticalTextAlignment)] = MapVerticalTextAlignment,
		};

	public EditorHandler() : base(Mapper)
	{
	}

	protected override Gtk.TextView CreatePlatformView()
	{
		var textView = Gtk.TextView.New();
		textView.SetWrapMode(Gtk.WrapMode.Word);
		return textView;
	}

	protected override void ConnectHandler(Gtk.TextView platformView)
	{
		base.ConnectHandler(platformView);
		var buffer = platformView.GetBuffer();
		if (buffer != null)
			buffer.OnChanged += OnBufferChanged;
	}

	protected override void DisconnectHandler(Gtk.TextView platformView)
	{
		var buffer = platformView.GetBuffer();
		if (buffer != null)
			buffer.OnChanged -= OnBufferChanged;
		base.DisconnectHandler(platformView);
	}

	void OnBufferChanged(Gtk.TextBuffer sender, EventArgs args)
	{
		if (VirtualView == null) return;

		// Enforce max length
		if (_maxLength is int max and > 0)
		{
			sender.GetStartIter(out var s);
			sender.GetEndIter(out var e);
			var text = sender.GetText(s, e, false);
			if (text != null && text.Length > max)
			{
				sender.OnChanged -= OnBufferChanged;
				sender.SetText(text[..max], -1);
				sender.OnChanged += OnBufferChanged;
			}
		}

		sender.GetStartIter(out var start);
		sender.GetEndIter(out var end);
		VirtualView.Text = sender.GetText(start, end, false);
	}

	public static void MapText(EditorHandler handler, IEditor editor)
	{
		var buffer = handler.PlatformView?.GetBuffer();
		if (buffer != null)
		{
			buffer.GetStartIter(out var start);
			buffer.GetEndIter(out var end);
			var currentText = buffer.GetText(start, end, false);
			if (currentText != editor.Text)
				buffer.SetText(editor.Text ?? string.Empty, -1);
		}
	}

	public static void MapIsReadOnly(EditorHandler handler, IEditor editor)
	{
		handler.PlatformView?.SetEditable(!editor.IsReadOnly);
	}

	public static void MapTextColor(EditorHandler handler, IEditor editor)
	{
		if (editor.TextColor != null)
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(editor.TextColor)};");
	}

	public static void MapFont(EditorHandler handler, IEditor editor)
	{
		var css = handler.BuildFontCss(editor.Font);
		if (!string.IsNullOrEmpty(css)) handler.ApplyCss(handler.PlatformView, css);
	}

	public static void MapPlaceholder(EditorHandler handler, IEditor editor)
	{
		// GTK TextView doesn't have built-in placeholder; would need overlay label.
	}

	public static void MapPlaceholderColor(EditorHandler handler, IEditor editor)
	{
		// Placeholder not natively supported on TextView; no-op until overlay is added.
	}

	public static void MapCharacterSpacing(EditorHandler handler, IEditor editor)
	{
		handler.ApplyCss(handler.PlatformView, $"letter-spacing: {editor.CharacterSpacing}px;");
	}

	public static void MapCursorPosition(EditorHandler handler, IEditor editor)
	{
		var buffer = handler.PlatformView?.GetBuffer();
		if (buffer == null) return;
		buffer.GetStartIter(out var start);
		buffer.GetEndIter(out var end);
		var textLen = buffer.GetText(start, end, false)?.Length ?? 0;
		var pos = Math.Clamp(editor.CursorPosition, 0, textLen);
		buffer.GetIterAtOffset(out var iter, pos);
		buffer.PlaceCursor(iter);
	}

	public static void MapSelectionLength(EditorHandler handler, IEditor editor)
	{
		var buffer = handler.PlatformView?.GetBuffer();
		if (buffer == null) return;
		buffer.GetStartIter(out var s);
		buffer.GetEndIter(out var e);
		var textLen = buffer.GetText(s, e, false)?.Length ?? 0;
		var selStart = Math.Clamp(editor.CursorPosition, 0, textLen);
		var selEnd = Math.Clamp(selStart + editor.SelectionLength, 0, textLen);
		buffer.GetIterAtOffset(out var startIter, selStart);
		buffer.GetIterAtOffset(out var endIter, selEnd);
		buffer.SelectRange(startIter, endIter);
	}

	public static void MapHorizontalTextAlignment(EditorHandler handler, IEditor editor)
	{
		handler.PlatformView?.SetJustification(editor.HorizontalTextAlignment switch
		{
			TextAlignment.Start => Gtk.Justification.Left,
			TextAlignment.Center => Gtk.Justification.Center,
			TextAlignment.End => Gtk.Justification.Right,
			_ => Gtk.Justification.Left
		});
	}

	public static void MapMaxLength(EditorHandler handler, IEditor editor)
	{
		handler._maxLength = editor.MaxLength > 0 ? editor.MaxLength : null;
	}

	public static void MapIsSpellCheckEnabled(EditorHandler handler, IEditor editor)
	{
		// GTK TextView does not have built-in spell-check; intentional no-op.
	}

	public static void MapIsTextPredictionEnabled(EditorHandler handler, IEditor editor)
	{
		// GTK TextView does not support text prediction; intentional no-op.
	}

	public static void MapKeyboard(EditorHandler handler, IEditor editor)
	{
		handler.PlatformView?.SetInputPurpose(EntryHandler.MapKeyboardToInputPurpose(editor.Keyboard));
	}

	public static void MapVerticalTextAlignment(EditorHandler handler, IEditor editor)
	{
		if (handler.PlatformView == null || editor is not ITextAlignment ta) return;
		handler.PlatformView.SetValign(ta.VerticalTextAlignment switch
		{
			TextAlignment.Start => Gtk.Align.Start,
			TextAlignment.Center => Gtk.Align.Center,
			TextAlignment.End => Gtk.Align.End,
			_ => Gtk.Align.Start
		});
	}
}
