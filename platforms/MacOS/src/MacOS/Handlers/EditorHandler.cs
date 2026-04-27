using Foundation;
using Microsoft.Maui.Handlers;
using AppKit;
using CoreGraphics;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public class EditorNSView : NSScrollView
{
    public PlaceholderNSTextView TextView { get; }

    public EditorNSView()
    {
        HasVerticalScroller = true;
        HasHorizontalScroller = false;
        AutohidesScrollers = true;

        TextView = new PlaceholderNSTextView
        {
            RichText = false,
            VerticallyResizable = true,
            HorizontallyResizable = false,
            AutoresizingMask = NSViewResizingMask.WidthSizable,
        };
        TextView.TextContainer!.WidthTracksTextView = true;

        DocumentView = TextView;
    }

    // Don't override IntrinsicContentSize — querying the layout manager
    // (GetUsedRect / EnsureLayoutForTextContainer) during an AppKit layout
    // pass re-invalidates the view, causing an infinite recursion.
    // MAUI controls sizing via HeightRequest and the layout system instead.
}

public class PlaceholderNSTextView : NSTextView
{
    string? _placeholder;

    public string? PlaceholderText
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            NeedsDisplay = true;
        }
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        base.DrawRect(dirtyRect);

        if (!string.IsNullOrEmpty(_placeholder) && string.IsNullOrEmpty(Value))
        {
            var attrs = new NSStringAttributes
            {
                ForegroundColor = NSColor.Gray,
                Font = Font ?? NSFont.SystemFontOfSize(NSFont.SystemFontSize),
            };

            var inset = TextContainerInset;
            var padding = TextContainer?.LineFragmentPadding ?? 5;
            var rect = new CGRect(
                padding + inset.Width,
                inset.Height,
                Bounds.Width - padding * 2 - inset.Width * 2,
                Bounds.Height - inset.Height * 2);

            var nsStr = new NSAttributedString(_placeholder, attrs);
            nsStr.DrawInRect(rect);
        }
    }
}

public class EditorHandler : MacOSViewHandler<IEditor, EditorNSView>
{
    public static readonly IPropertyMapper<IEditor, EditorHandler> Mapper =
        new PropertyMapper<IEditor, EditorHandler>(ViewMapper)
        {
            [nameof(ITextInput.Text)] = MapText,
            [nameof(ITextStyle.TextColor)] = MapTextColor,
            [nameof(ITextStyle.Font)] = MapFont,
            [nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
            [nameof(IPlaceholder.Placeholder)] = MapPlaceholder,
            [nameof(ITextInput.IsReadOnly)] = MapIsReadOnly,
            [nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
            [nameof(ITextInput.MaxLength)] = MapMaxLength,
            [nameof(IView.IsEnabled)] = MapIsEnabled,
        };

    bool _updating;

    public EditorHandler() : base(Mapper) { }

    protected override EditorNSView CreatePlatformView() => new();

    protected override void ConnectHandler(EditorNSView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextView.TextDidChange += OnTextChanged;
    }

    protected override void DisconnectHandler(EditorNSView platformView)
    {
        platformView.TextView.TextDidChange -= OnTextChanged;
        base.DisconnectHandler(platformView);
    }

    void OnTextChanged(object? sender, EventArgs e)
    {
        if (_updating || VirtualView == null)
            return;

        _updating = true;
        try
        {
            VirtualView.Text = PlatformView.TextView.Value ?? string.Empty;
            PlatformView.TextView.NeedsDisplay = true;
        }
        finally
        {
            _updating = false;
        }
    }

    public static void MapText(EditorHandler handler, IEditor editor)
    {
        if (handler._updating)
            return;

        handler.PlatformView.TextView.Value = editor.Text ?? string.Empty;
        handler.PlatformView.TextView.NeedsDisplay = true;
    }

    public static void MapTextColor(EditorHandler handler, IEditor editor)
    {
        if (editor.TextColor is not null)
            handler.PlatformView.TextView.TextColor = editor.TextColor.ToPlatformColor();
    }

    public static void MapFont(EditorHandler handler, IEditor editor)
    {
        handler.PlatformView.TextView.Font = editor.Font.ToNSFont();
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    public static void MapPlaceholder(EditorHandler handler, IEditor editor)
    {
        if (editor is IPlaceholder placeholder)
        {
            handler.PlatformView.TextView.PlaceholderText = placeholder.Placeholder;
            handler.PlatformView.TextView.AccessibilityPlaceholderValue = placeholder.Placeholder;
        }
    }

    public static void MapCharacterSpacing(EditorHandler handler, IEditor editor)
    {
        if (editor.CharacterSpacing != 0)
        {
            var text = handler.PlatformView.TextView.Value ?? string.Empty;
            var storage = handler.PlatformView.TextView.TextStorage;
            if (storage != null && text.Length > 0)
            {
                storage.AddAttribute(NSStringAttributeKey.KerningAdjustment,
                    NSNumber.FromDouble(editor.CharacterSpacing), new NSRange(0, text.Length));
            }
        }
    }

    public static void MapIsReadOnly(EditorHandler handler, IEditor editor)
    {
        handler.PlatformView.TextView.Editable = !editor.IsReadOnly;
    }

    public static new void MapIsEnabled(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is EditorNSView editorView)
        {
            editorView.TextView.Editable = view.IsEnabled;
            editorView.TextView.Selectable = view.IsEnabled;
            editorView.AlphaValue = view.IsEnabled ? 1.0f : 0.5f;
        }
    }

    public static void MapHorizontalTextAlignment(EditorHandler handler, IEditor editor)
    {
        handler.PlatformView.TextView.Alignment = editor.HorizontalTextAlignment switch
        {
            TextAlignment.Center => NSTextAlignment.Center,
            TextAlignment.End => NSTextAlignment.Right,
            _ => NSTextAlignment.Left,
        };
    }

    public static void MapMaxLength(EditorHandler handler, IEditor editor)
    {
        if (editor.MaxLength >= 0)
        {
            var currentText = handler.PlatformView.TextView.Value ?? string.Empty;
            if (currentText.Length > editor.MaxLength)
                handler.PlatformView.TextView.Value = currentText[..editor.MaxLength];
        }
    }
}
