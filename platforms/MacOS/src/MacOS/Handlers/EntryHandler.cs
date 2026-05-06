using Foundation;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class EntryHandler : MacOSViewHandler<IEntry, NSTextField>
{
    public static readonly IPropertyMapper<IEntry, EntryHandler> Mapper =
        new PropertyMapper<IEntry, EntryHandler>(ViewMapper)
        {
            [nameof(ITextInput.Text)] = MapText,
            [nameof(ITextStyle.TextColor)] = MapTextColor,
            [nameof(ITextStyle.Font)] = MapFont,
            [nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
            [nameof(IPlaceholder.Placeholder)] = MapPlaceholder,
            [nameof(IPlaceholder.PlaceholderColor)] = MapPlaceholderColor,
            [nameof(IEntry.IsPassword)] = MapIsPassword,
            [nameof(IEntry.ReturnType)] = MapReturnType,
            [nameof(ITextInput.IsReadOnly)] = MapIsReadOnly,
            [nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
            [nameof(ITextInput.MaxLength)] = MapMaxLength,
            [nameof(IEntry.CursorPosition)] = MapCursorPosition,
            [nameof(IEntry.SelectionLength)] = MapSelectionLength,
            [nameof(IEntry.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
        };

    bool _updating;

    public EntryHandler() : base(Mapper)
    {
    }

    protected override NSTextField CreatePlatformView()
    {
        return new NSTextField
        {
            Bordered = true,
            Bezeled = true,
            BezelStyle = NSTextFieldBezelStyle.Rounded,
        };
    }

    protected override void ConnectHandler(NSTextField platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Changed += OnTextChanged;
        platformView.EditingEnded += OnEditingEnded;
    }

    protected override void DisconnectHandler(NSTextField platformView)
    {
        platformView.Changed -= OnTextChanged;
        platformView.EditingEnded -= OnEditingEnded;
        base.DisconnectHandler(platformView);
    }

    internal void SetPlatformView(NSTextField newView)
    {
        // Use reflection to update the handler's PlatformView reference
        // since ViewHandler doesn't expose a public setter
        var prop = typeof(ViewHandler).GetProperty("PlatformView");
        prop?.SetValue(this, newView);
    }

    internal void OnTextChanged(object? sender, EventArgs e)
    {
        if (_updating || VirtualView == null)
            return;

        _updating = true;
        try
        {
            if (VirtualView is ITextInput textInput)
                textInput.Text = PlatformView.StringValue ?? string.Empty;
        }
        finally
        {
            _updating = false;
        }
    }

    internal void OnEditingEnded(object? sender, EventArgs e)
    {
        VirtualView?.Completed();
    }

    public static void MapText(EntryHandler handler, IEntry entry)
    {
        if (handler._updating)
            return;

        if (entry is ITextInput textInput)
            handler.PlatformView.StringValue = textInput.Text ?? string.Empty;
    }

    public static void MapTextColor(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextStyle textStyle && textStyle.TextColor != null)
            handler.PlatformView.TextColor = textStyle.TextColor.ToPlatformColor();
    }

    public static void MapFont(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextStyle textStyle)
        {
            handler.PlatformView.Font = textStyle.Font.ToNSFont();
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
    }

    public static void MapPlaceholder(EntryHandler handler, IEntry entry)
    {
        if (entry is IPlaceholder placeholder)
            handler.PlatformView.PlaceholderString = placeholder.Placeholder ?? string.Empty;
    }

    public static void MapPlaceholderColor(EntryHandler handler, IEntry entry)
    {
        if (entry is IPlaceholder placeholder && placeholder.PlaceholderColor != null)
        {
            var attributes = new NSDictionary(
                NSStringAttributeKey.ForegroundColor,
                placeholder.PlaceholderColor.ToPlatformColor());

            handler.PlatformView.PlaceholderAttributedString = new NSAttributedString(
                placeholder.Placeholder ?? string.Empty,
                attributes);
        }
    }

    public static void MapIsPassword(EntryHandler handler, IEntry entry)
    {
        // NSSecureTextField is a separate class on macOS.
        // We swap between NSTextField and NSSecureTextField by rebuilding the platform view.
        var currentView = handler.PlatformView;
        bool isCurrentlySecure = currentView is NSSecureTextField;

        if (entry.IsPassword == isCurrentlySecure)
            return;

        // Preserve state before swap
        var text = currentView.StringValue;
        var frame = currentView.Frame;

        // Disconnect old view
        currentView.Changed -= handler.OnTextChanged;
        currentView.EditingEnded -= handler.OnEditingEnded;

        NSTextField newView;
        if (entry.IsPassword)
        {
            newView = new NSSecureTextField
            {
                Bordered = true,
                Bezeled = true,
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Frame = frame,
                StringValue = text ?? string.Empty,
            };
        }
        else
        {
            newView = new NSTextField
            {
                Bordered = true,
                Bezeled = true,
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Frame = frame,
                StringValue = text ?? string.Empty,
            };
        }

        // Replace in view hierarchy
        var superview = currentView.Superview;
        if (superview != null)
        {
            superview.ReplaceSubviewWith(currentView, newView);
        }

        // Connect new view
        newView.Changed += handler.OnTextChanged;
        newView.EditingEnded += handler.OnEditingEnded;

        // Update handler's platform view reference
        handler.SetPlatformView(newView);
    }

    public static void MapIsReadOnly(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextInput textInput)
            handler.PlatformView.Editable = !textInput.IsReadOnly;
    }

    public static void MapHorizontalTextAlignment(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextAlignment textAlignment)
        {
            handler.PlatformView.Alignment = textAlignment.HorizontalTextAlignment switch
            {
                TextAlignment.Center => NSTextAlignment.Center,
                TextAlignment.End => NSTextAlignment.Right,
                _ => NSTextAlignment.Left,
            };
        }
    }

    public static void MapMaxLength(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextInput textInput && textInput.MaxLength >= 0)
        {
            var currentText = handler.PlatformView.StringValue ?? string.Empty;
            if (currentText.Length > textInput.MaxLength)
                handler.PlatformView.StringValue = currentText[..textInput.MaxLength];
        }
    }

    public static void MapCharacterSpacing(EntryHandler handler, IEntry entry)
    {
        if (entry is ITextStyle textStyle && textStyle.CharacterSpacing != 0)
        {
            var text = handler.PlatformView.StringValue ?? string.Empty;
            var attrStr = new NSMutableAttributedString(text);
            attrStr.AddAttribute(NSStringAttributeKey.KerningAdjustment,
                NSNumber.FromDouble(textStyle.CharacterSpacing), new NSRange(0, text.Length));
            handler.PlatformView.AttributedStringValue = attrStr;
        }
    }

    public static void MapReturnType(EntryHandler handler, IEntry entry)
    {
        // macOS NSTextField doesn't have a ReturnType concept like iOS keyboard return key.
        // The return key always submits the field on macOS.
    }

    public static void MapCursorPosition(EntryHandler handler, IEntry entry)
    {
        if (handler.PlatformView.CurrentEditor is NSTextView editor)
        {
            var position = Math.Min(entry.CursorPosition, (handler.PlatformView.StringValue ?? string.Empty).Length);
            editor.SetSelectedRange(new NSRange(position, 0));
        }
    }

    public static void MapSelectionLength(EntryHandler handler, IEntry entry)
    {
        if (handler.PlatformView.CurrentEditor is NSTextView editor)
        {
            var text = handler.PlatformView.StringValue ?? string.Empty;
            var start = Math.Min(entry.CursorPosition, text.Length);
            var length = Math.Min(entry.SelectionLength, text.Length - start);
            editor.SetSelectedRange(new NSRange(start, length));
        }
    }

    public static void MapIsTextPredictionEnabled(EntryHandler handler, IEntry entry)
    {
        // macOS text prediction/autocomplete can be controlled via NSTextView settings
        if (handler.PlatformView.CurrentEditor is NSTextView editor)
        {
            editor.AutomaticTextCompletionEnabled = entry.IsTextPredictionEnabled;
        }
    }
}
