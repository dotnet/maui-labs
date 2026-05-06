using Microsoft.Maui.Handlers;
using AppKit;
using CoreGraphics;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

class MauiNSButton : NSButton
{
    static readonly Thickness DefaultNonBorderedPadding = new Thickness(8, 4, 8, 4);
    Thickness _padding;
    internal Graphics.LinearGradientPaint? _linearGradient;
    internal Graphics.RadialGradientPaint? _radialGradient;
    internal int _cornerRadius = -1;
    bool _isPressed;

    public Thickness MauiPadding
    {
        get => _padding;
        set
        {
            _padding = value;
            InvalidateIntrinsicContentSize();
            NeedsDisplay = true;
        }
    }

    Thickness EffectivePadding
    {
        get
        {
            if (_padding != default && !double.IsNaN(_padding.Left))
                return _padding;
            // When Bordered=false (custom background), native bezel padding is lost;
            // apply a default so content (especially images) doesn't sit flush at edges
            if (!Bordered)
                return DefaultNonBorderedPadding;
            return default;
        }
    }

    public override CGSize IntrinsicContentSize
    {
        get
        {
            // Compute content size without calling base.IntrinsicContentSize,
            // which can trigger recursive auto-layout in AppKit → NaN → exception.
            CGSize size;
            if (AttributedTitle?.Length > 0)
            {
                size = AttributedTitle.Size;
            }
            else if (!string.IsNullOrEmpty(Title))
            {
                var font = Font ?? NSFont.SystemFontOfSize(NSFont.SystemFontSize);
                var attrs = new NSStringAttributes { Font = font };
                size = new NSAttributedString(Title, attrs).Size;
            }
            else
            {
                size = CGSize.Empty;
            }

            if (Image != null)
            {
                var imgSize = Image.Size;
                size.Width += imgSize.Width;
                if (imgSize.Height > size.Height)
                    size.Height = imgSize.Height;
                if (AttributedTitle?.Length > 0 || !string.IsNullOrEmpty(Title))
                    size.Width += 4; // spacing between image and title
            }

            // If no content, return NoIntrinsicMetric
            if (size.Width <= 0 && size.Height <= 0)
                return new CGSize(NSView.NoIntrinsicMetric, NSView.NoIntrinsicMetric);

            var pad = EffectivePadding;
            if (Bordered)
            {
                size.Width += 20;
                size.Height += 8;
            }
            else if (pad != default)
            {
                size.Width += (nfloat)(pad.Left + pad.Right);
                size.Height += (nfloat)(pad.Top + pad.Bottom);
            }

            if (size.Height < 21)
                size.Height = 21;

            if (nfloat.IsNaN(size.Width) || nfloat.IsNaN(size.Height))
                return new CGSize(NSView.NoIntrinsicMetric, NSView.NoIntrinsicMetric);

            return size;
        }
    }

    public override void MouseDown(NSEvent theEvent)
    {
        if (_linearGradient != null || _radialGradient != null)
        {
            _isPressed = true;
            NeedsDisplay = true;
        }
        base.MouseDown(theEvent); // blocks until mouse up
        if (_linearGradient != null || _radialGradient != null)
        {
            _isPressed = false;
            NeedsDisplay = true;
        }
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        // Draw gradient background if set
        if (_linearGradient != null || _radialGradient != null)
        {
            var context = NSGraphicsContext.CurrentContext?.CGContext;
            if (context != null)
            {
                var stops = _linearGradient?.GradientStops ?? _radialGradient?.GradientStops;
                if (stops != null && stops.Length > 0)
                {
                    context.SaveState();

                    // Clip to rounded rect for button shape
                    var cornerRadius = _cornerRadius >= 0 ? (nfloat)_cornerRadius : (nfloat)6;
                    var path = NSBezierPath.FromRoundedRect(Bounds, cornerRadius, cornerRadius);
                    path.AddClip();

                    var colors = new CGColor[stops.Length];
                    var locations = new nfloat[stops.Length];
                    for (int i = 0; i < stops.Length; i++)
                    {
                        colors[i] = stops[i].Color.ToPlatformColor().CGColor;
                        locations[i] = stops[i].Offset;
                    }
                    using var colorSpace = CGColorSpace.CreateSrgb();
                    using var gradient = new CGGradient(colorSpace, colors, locations);

                    if (_linearGradient != null)
                    {
                        var start = new CGPoint(Bounds.Width * _linearGradient.StartPoint.X,
                                                Bounds.Height * _linearGradient.StartPoint.Y);
                        var end = new CGPoint(Bounds.Width * _linearGradient.EndPoint.X,
                                              Bounds.Height * _linearGradient.EndPoint.Y);
                        context.DrawLinearGradient(gradient, start, end,
                            CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation);
                    }
                    else if (_radialGradient != null)
                    {
                        var center = new CGPoint(Bounds.Width * _radialGradient.Center.X,
                                                 Bounds.Height * _radialGradient.Center.Y);
                        var radius = (nfloat)Math.Max(Bounds.Width, Bounds.Height) * (nfloat)_radialGradient.Radius;
                        context.DrawRadialGradient(gradient, center, (nfloat)0, center, radius,
                            CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation);
                    }

                    // Darken when pressed
                    if (_isPressed)
                    {
                        context.SetFillColor(new CGColor(0, 0, 0, 0.25f));
                        context.FillRect(Bounds);
                    }

                    context.RestoreState();
                }
            }
        }

        var pad = EffectivePadding;
        if (pad != default && !Bordered)
        {
            // Inset the drawing frame so cell content (text + image) is padded
            var paddedFrame = new CGRect(
                (nfloat)pad.Left,
                IsFlipped ? (nfloat)pad.Top : (nfloat)pad.Bottom,
                Bounds.Width - (nfloat)(pad.Left + pad.Right),
                Bounds.Height - (nfloat)(pad.Top + pad.Bottom)
            );
            Cell.DrawInteriorWithFrame(paddedFrame, this);
        }
        else
        {
            base.DrawRect(dirtyRect);
        }
    }
}

public partial class ButtonHandler : MacOSViewHandler<IButton, NSButton>
{
    public static readonly IPropertyMapper<IButton, ButtonHandler> Mapper =
        new PropertyMapper<IButton, ButtonHandler>(ViewMapper)
        {
            [nameof(IText.Text)] = MapText,
            [nameof(ITextStyle.TextColor)] = MapTextColor,
            [nameof(ITextStyle.Font)] = MapFont,
            [nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
            [nameof(IView.Background)] = MapBackground,
            [nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
            [nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
            [nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
            [nameof(IPadding.Padding)] = MapPadding,
            [nameof(IImage.Source)] = MapImageSource,
            ["ContentLayout"] = MapContentLayout,
        };

    public ButtonHandler() : base(Mapper)
    {
    }

    protected override NSButton CreatePlatformView()
    {
        var button = new MauiNSButton
        {
            BezelStyle = NSBezelStyle.Rounded,
            Title = string.Empty,
        };
        return button;
    }

    protected override void ConnectHandler(NSButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
    }

    protected override void DisconnectHandler(NSButton platformView)
    {
        platformView.Activated -= OnActivated;
        base.DisconnectHandler(platformView);
    }

    void OnActivated(object? sender, EventArgs e)
    {
        VirtualView?.Clicked();
    }

    public static void MapText(ButtonHandler handler, IButton button)
    {
        if (button is IText textButton)
        {
            handler.PlatformView.Title = textButton.Text ?? string.Empty;
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
    }

    public static void MapTextColor(ButtonHandler handler, IButton button)
    {
        if (button is ITextStyle textStyle && textStyle.TextColor != null)
            handler.PlatformView.ContentTintColor = textStyle.TextColor.ToPlatformColor();
    }

    public static void MapFont(ButtonHandler handler, IButton button)
    {
        if (button is ITextStyle textStyle)
        {
            handler.PlatformView.Font = textStyle.Font.ToNSFont();
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
    }

    public static void MapCharacterSpacing(ButtonHandler handler, IButton button)
    {
        if (button is ITextStyle textStyle)
        {
            var title = handler.PlatformView.Title ?? string.Empty;
            var attributedTitle = new NSMutableAttributedString(title);
            if (title.Length > 0)
            {
                var range = new NSRange(0, title.Length);
                attributedTitle.AddAttribute(NSStringAttributeKey.KerningAdjustment,
                    new NSNumber(textStyle.CharacterSpacing), range);

                if (textStyle.TextColor != null)
                    attributedTitle.AddAttribute(NSStringAttributeKey.ForegroundColor,
                        textStyle.TextColor.ToPlatformColor(), range);

                attributedTitle.AddAttribute(NSStringAttributeKey.Font,
                    handler.PlatformView.Font, range);
            }
            handler.PlatformView.AttributedTitle = attributedTitle;
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
    }

    public static void MapBackground(ButtonHandler handler, IButton button)
    {
        var wasBordered = handler.PlatformView.Bordered;
        var btn = handler.PlatformView as MauiNSButton;

        // Clear previous gradient
        if (btn != null)
        {
            btn._linearGradient = null;
            btn._radialGradient = null;
        }

        if (button.Background is Graphics.LinearGradientPaint linear)
        {
            handler.PlatformView.Bordered = false;
            if (btn != null)
            {
                btn._linearGradient = linear;
                btn._radialGradient = null;
                btn.WantsLayer = false;
            }
            handler.PlatformView.NeedsDisplay = true;
        }
        else if (button.Background is Graphics.RadialGradientPaint radial)
        {
            handler.PlatformView.Bordered = false;
            if (btn != null)
            {
                btn._linearGradient = null;
                btn._radialGradient = radial;
                btn.WantsLayer = false;
            }
            handler.PlatformView.NeedsDisplay = true;
        }
        else
        {
            // Extract color from background paint or BackgroundColor
            Graphics.Color? bgColor = null;
            if (button.Background is Graphics.SolidPaint solidPaint)
                bgColor = solidPaint.Color;

            // Fallback: check BackgroundColor directly (handles SolidColorBrush / ImmutableBrush)
            if (bgColor == null && button is Microsoft.Maui.Controls.Button mauiButton && mauiButton.BackgroundColor != null)
                bgColor = mauiButton.BackgroundColor;

            if (bgColor != null)
            {
                handler.PlatformView.WantsLayer = true;
                handler.PlatformView.Bordered = false;
                handler.PlatformView.Layer!.BackgroundColor = bgColor.ToPlatformColor().CGColor;
            }
            else
            {
                handler.PlatformView.Bordered = true;
                handler.PlatformView.BezelStyle = NSBezelStyle.Rounded;
            }
        }

        // Bordered state affects default padding — invalidate both native and MAUI layout
        if (wasBordered != handler.PlatformView.Bordered)
        {
            handler.PlatformView.InvalidateIntrinsicContentSize();
            handler.PlatformView.NeedsDisplay = true;
            handler.VirtualView?.InvalidateMeasure();
        }
    }

    public static void MapCornerRadius(ButtonHandler handler, IButton button)
    {
        if (button is IButtonStroke stroke && stroke.CornerRadius >= 0)
        {
            if (handler.PlatformView is MauiNSButton btn)
                btn._cornerRadius = stroke.CornerRadius;
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.CornerRadius = (nfloat)stroke.CornerRadius;
            handler.PlatformView.NeedsDisplay = true;
        }
    }

    public static void MapStrokeColor(ButtonHandler handler, IButton button)
    {
        if (button is IButtonStroke stroke && stroke.StrokeColor != null)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.BorderColor = stroke.StrokeColor.ToPlatformColor().CGColor;
        }
    }

    public static void MapStrokeThickness(ButtonHandler handler, IButton button)
    {
        if (button is IButtonStroke stroke && stroke.StrokeThickness >= 0)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.BorderWidth = (nfloat)stroke.StrokeThickness;
        }
    }

    public static void MapPadding(ButtonHandler handler, IButton button)
    {
        if (handler.PlatformView is MauiNSButton mauiButton && button is IPadding padded)
        {
            mauiButton.MauiPadding = padded.Padding;
        }
    }

    public static void MapImageSource(ButtonHandler handler, IButton button)
    {
        if (button is not IImage imageButton)
            return;

        if (imageButton.Source is IFileImageSource fileSource)
        {
            handler.PlatformView.Image = new AppKit.NSImage(fileSource.File);
        }
        else if (imageButton.Source is IFontImageSource fontSource)
        {
            handler.PlatformView.Image = FontImageSourceHelper.CreateImage(fontSource, handler.MauiContext);
        }
        else if (imageButton.Source is IUriImageSource uriSource)
        {
            _ = LoadButtonImageFromUri(handler, uriSource.Uri);
            return; // async — position set in callback
        }
        else
        {
            handler.PlatformView.Image = null;
        }

        ApplyImagePosition(handler, button);
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    public static void MapContentLayout(ButtonHandler handler, IButton button)
    {
        ApplyImagePosition(handler, button);
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    static void ApplyImagePosition(ButtonHandler handler, IButton button)
    {
        if (handler.PlatformView.Image == null)
        {
            handler.PlatformView.ImagePosition = NSCellImagePosition.NoImage;
            return;
        }

        var position = NSCellImagePosition.ImageLeft;
        if (button is Microsoft.Maui.Controls.Button mauiButton)
        {
            position = mauiButton.ContentLayout.Position switch
            {
                Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Left => NSCellImagePosition.ImageLeft,
                Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Right => NSCellImagePosition.ImageRight,
                Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top => NSCellImagePosition.ImageAbove,
                Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Bottom => NSCellImagePosition.ImageBelow,
                _ => NSCellImagePosition.ImageLeft,
            };
        }
        handler.PlatformView.ImagePosition = position;
    }

    static async Task LoadButtonImageFromUri(ButtonHandler handler, Uri uri)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var data = await client.GetByteArrayAsync(uri);
            var nsImage = new AppKit.NSImage(Foundation.NSData.FromArray(data));
            handler.PlatformView.Image = nsImage;
            ApplyImagePosition(handler, handler.VirtualView);
            handler.PlatformView.InvalidateIntrinsicContentSize();
        }
        catch { }
    }
}
