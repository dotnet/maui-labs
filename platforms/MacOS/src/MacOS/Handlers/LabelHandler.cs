using Microsoft.Maui.Handlers;
using Microsoft.Maui.Controls;
using AppKit;
using CoreGraphics;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class LabelHandler : MacOSViewHandler<ILabel, MauiNSTextField>
{
    public static readonly IPropertyMapper<ILabel, LabelHandler> Mapper =
        new PropertyMapper<ILabel, LabelHandler>(ViewMapper)
        {
            [nameof(ILabel.Text)] = MapText,
            [nameof(ILabel.TextColor)] = MapTextColor,
            [nameof(ILabel.Font)] = MapFont,
            [nameof(ILabel.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
            [nameof(Label.LineBreakMode)] = MapLineBreakMode,
            [nameof(Label.MaxLines)] = MapMaxLines,
            [nameof(ILabel.TextDecorations)] = MapTextDecorations,
            [nameof(ILabel.CharacterSpacing)] = MapCharacterSpacing,
            [nameof(ILabel.Padding)] = MapPadding,
            [nameof(Label.FormattedText)] = MapFormattedText,
            [nameof(Label.LineHeight)] = MapLineHeight,
            [nameof(Label.TextType)] = MapTextType,
            [nameof(Label.TextTransform)] = MapTextTransform,
        };

    public LabelHandler() : base(Mapper)
    {
    }

    protected override MauiNSTextField CreatePlatformView()
    {
        var label = new MauiNSTextField();
        label.Editable = false;
        label.Selectable = false;
        label.Bordered = false;
        label.DrawsBackground = false;
        label.TextColor = NSColor.ControlText;
        label.MaximumNumberOfLines = 0;
        return label;
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var platformView = PlatformView;
        if (platformView == null)
            return Size.Zero;

        if (double.IsNaN(widthConstraint))
            widthConstraint = double.PositiveInfinity;
        if (double.IsNaN(heightConstraint))
            heightConstraint = double.PositiveInfinity;

        var insets = platformView.TextInsets;
        var hInsets = (double)insets.Left + (double)insets.Right;
        var vInsets = (double)insets.Top + (double)insets.Bottom;

        // Reduce constraint by padding before measuring text
        var textWidth = double.IsPositiveInfinity(widthConstraint) ? widthConstraint : widthConstraint - hInsets;
        var textHeight = double.IsPositiveInfinity(heightConstraint) ? heightConstraint : heightConstraint - vInsets;
        var widthConstrained = !double.IsPositiveInfinity(textWidth);

        // Use NSCell.CellSizeForBounds for accurate text measurement
        var cell = platformView.Cell;
        var constraintRect = new CGRect(0, 0,
            widthConstrained ? (nfloat)textWidth : (nfloat)10000,
            !double.IsPositiveInfinity(textHeight) ? (nfloat)textHeight : (nfloat)10000);
        var cellSize = cell.CellSizeForBounds(constraintRect);

        var measuredWidth = Math.Ceiling((double)cellSize.Width) + hInsets;
        var measuredHeight = Math.Ceiling((double)cellSize.Height) + vInsets;

        if (!double.IsPositiveInfinity(widthConstraint))
            measuredWidth = Math.Min(measuredWidth, widthConstraint);

        // Apply explicit MAUI dimensions
        var vw = VirtualView;
        if (IsExplicitSize(vw.Width))
            measuredWidth = vw.Width;
        if (IsExplicitSize(vw.Height))
            measuredHeight = vw.Height;

        if (IsExplicitSize(vw.MinimumWidth))
            measuredWidth = Math.Max(measuredWidth, vw.MinimumWidth);
        if (IsExplicitSize(vw.MinimumHeight))
            measuredHeight = Math.Max(measuredHeight, vw.MinimumHeight);
        if (IsExplicitSize(vw.MaximumWidth))
            measuredWidth = Math.Min(measuredWidth, vw.MaximumWidth);
        if (IsExplicitSize(vw.MaximumHeight))
            measuredHeight = Math.Min(measuredHeight, vw.MaximumHeight);

        return new Size(measuredWidth, measuredHeight);
    }

    static bool IsExplicitSize(double value)
        => !double.IsNaN(value) && value >= 0 && !double.IsPositiveInfinity(value);

    // Unified attributed text builder — all text-related mappers call this to avoid
    // conflicts between StringValue, Font, and AttributedStringValue on NSTextField.
    static void UpdateAttributedText(LabelHandler handler, ILabel label)
    {
        if (label is Label mauiLabel && mauiLabel.FormattedText != null)
            return; // FormattedText handled separately

        var pv = handler.PlatformView;
        var text = label.Text ?? string.Empty;

        // Apply TextTransform
        if (label is Label mauiLbl)
        {
            text = mauiLbl.TextTransform switch
            {
                TextTransform.Uppercase => text.ToUpperInvariant(),
                TextTransform.Lowercase => text.ToLowerInvariant(),
                _ => text,
            };
        }

        // HTML text type — parse HTML into attributed string
        if (label is Label ml2 && ml2.TextType == TextType.Html)
        {
            UpdateHtmlText(handler, label, text);
            return;
        }

        var attrs = new NSMutableDictionary();

        // Font
        attrs[NSStringAttributeKey.Font] = label.Font.ToNSFont();

        // TextColor
        if (label.TextColor != null)
            attrs[NSStringAttributeKey.ForegroundColor] = label.TextColor.ToPlatformColor();

        // CharacterSpacing
        if (label.CharacterSpacing != 0)
            attrs[NSStringAttributeKey.KerningAdjustment] = NSNumber.FromDouble(label.CharacterSpacing);

        // TextDecorations
        if (label.TextDecorations.HasFlag(TextDecorations.Underline))
            attrs[NSStringAttributeKey.UnderlineStyle] = NSNumber.FromInt32((int)NSUnderlineStyle.Single);
        if (label.TextDecorations.HasFlag(TextDecorations.Strikethrough))
            attrs[NSStringAttributeKey.StrikethroughStyle] = NSNumber.FromInt32((int)NSUnderlineStyle.Single);

        // LineHeight & Alignment via paragraph style
        var paraStyle = new NSMutableParagraphStyle();
        paraStyle.Alignment = label.HorizontalTextAlignment switch
        {
            TextAlignment.Center => NSTextAlignment.Center,
            TextAlignment.End => NSTextAlignment.Right,
            _ => NSTextAlignment.Left,
        };
        if (label is Label ml && ml.LineHeight >= 0 && ml.LineHeight != 1)
            paraStyle.LineHeightMultiple = (nfloat)ml.LineHeight;
        if (label is Label lbl)
        {
            paraStyle.LineBreakMode = lbl.LineBreakMode switch
            {
                LineBreakMode.NoWrap => NSLineBreakMode.Clipping,
                LineBreakMode.CharacterWrap => NSLineBreakMode.CharWrapping,
                LineBreakMode.HeadTruncation => NSLineBreakMode.TruncatingHead,
                LineBreakMode.TailTruncation => NSLineBreakMode.TruncatingTail,
                LineBreakMode.MiddleTruncation => NSLineBreakMode.TruncatingMiddle,
                _ => NSLineBreakMode.ByWordWrapping,
            };
        }
        attrs[NSStringAttributeKey.ParagraphStyle] = paraStyle;

        pv.AttributedStringValue = new NSAttributedString(text, attrs);
        pv.InvalidateIntrinsicContentSize();
    }

    static void UpdateHtmlText(LabelHandler handler, ILabel label, string html)
    {
        var pv = handler.PlatformView;

        // Wrap HTML with default font styling so the base font matches the label's font
        var font = label.Font.ToNSFont();
        var fontSize = font.PointSize;
        var fontFamily = font.FamilyName ?? "system-ui";
        var colorCss = "";
        if (label.TextColor != null)
        {
            var c = label.TextColor;
            colorCss = $"color: rgba({(int)(c.Red * 255)},{(int)(c.Green * 255)},{(int)(c.Blue * 255)},{c.Alpha});";
        }
        var styledHtml = $"<div style=\"font-family: '{fontFamily}', system-ui; font-size: {fontSize}px; {colorCss}\">{html}</div>";

        var data = NSData.FromString(styledHtml, NSStringEncoding.UTF8);
        var options = new NSAttributedStringDocumentAttributes
        {
            DocumentType = NSDocumentType.HTML,
            StringEncoding = NSStringEncoding.UTF8,
        };

        NSDictionary? resultAttrs;
        var attrStr = new NSAttributedString(data, options, out resultAttrs);
        if (attrStr != null)
        {
            // Apply paragraph style for alignment and line break mode
            var mutable = new NSMutableAttributedString(attrStr);
            var paraStyle = new NSMutableParagraphStyle();
            paraStyle.Alignment = label.HorizontalTextAlignment switch
            {
                TextAlignment.Center => NSTextAlignment.Center,
                TextAlignment.End => NSTextAlignment.Right,
                _ => NSTextAlignment.Left,
            };
            if (label is Label lbl)
            {
                paraStyle.LineBreakMode = lbl.LineBreakMode switch
                {
                    LineBreakMode.NoWrap => NSLineBreakMode.Clipping,
                    LineBreakMode.CharacterWrap => NSLineBreakMode.CharWrapping,
                    LineBreakMode.HeadTruncation => NSLineBreakMode.TruncatingHead,
                    LineBreakMode.TailTruncation => NSLineBreakMode.TruncatingTail,
                    LineBreakMode.MiddleTruncation => NSLineBreakMode.TruncatingMiddle,
                    _ => NSLineBreakMode.ByWordWrapping,
                };
            }
            var fullRange = new NSRange(0, mutable.Length);
            mutable.AddAttribute(NSStringAttributeKey.ParagraphStyle, paraStyle, fullRange);
            pv.AttributedStringValue = mutable;
        }
        else
        {
            // Fallback to plain text if HTML parsing fails
            pv.StringValue = html;
        }
        pv.InvalidateIntrinsicContentSize();
    }

    public static void MapText(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapTextType(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapTextColor(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapFont(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapCharacterSpacing(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapTextDecorations(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapLineHeight(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);
    public static void MapTextTransform(LabelHandler handler, ILabel label) => UpdateAttributedText(handler, label);

    public static void MapHorizontalTextAlignment(LabelHandler handler, ILabel label)
    {
        handler.PlatformView.Alignment = label.HorizontalTextAlignment switch
        {
            TextAlignment.Center => NSTextAlignment.Center,
            TextAlignment.End => NSTextAlignment.Right,
            _ => NSTextAlignment.Left,
        };
        UpdateAttributedText(handler, label);
    }

    public static void MapLineBreakMode(LabelHandler handler, ILabel label)
    {
        if (label is not Label mauiLabel)
            return;

        handler.PlatformView.LineBreakMode = mauiLabel.LineBreakMode switch
        {
            LineBreakMode.NoWrap => NSLineBreakMode.Clipping,
            LineBreakMode.CharacterWrap => NSLineBreakMode.CharWrapping,
            LineBreakMode.HeadTruncation => NSLineBreakMode.TruncatingHead,
            LineBreakMode.TailTruncation => NSLineBreakMode.TruncatingTail,
            LineBreakMode.MiddleTruncation => NSLineBreakMode.TruncatingMiddle,
            _ => NSLineBreakMode.ByWordWrapping,
        };
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    public static void MapMaxLines(LabelHandler handler, ILabel label)
    {
        if (label is Label mauiLabel)
            handler.PlatformView.MaximumNumberOfLines = mauiLabel.MaxLines;
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    public static void MapPadding(LabelHandler handler, ILabel label)
    {
        var padding = label.Padding;
        handler.PlatformView.TextInsets = new AppKit.NSEdgeInsets(
            (nfloat)padding.Top, (nfloat)padding.Left,
            (nfloat)padding.Bottom, (nfloat)padding.Right);
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }

    public static void MapFormattedText(LabelHandler handler, ILabel label)
    {
        if (label is not Label mauiLabel)
            return;

        var formattedText = mauiLabel.FormattedText;
        if (formattedText == null || formattedText.Spans.Count == 0)
        {
            handler.PlatformView.StringValue = label.Text ?? string.Empty;
            return;
        }

        var attributed = new NSMutableAttributedString();

        foreach (var span in formattedText.Spans)
        {
            var text = span.Text ?? string.Empty;
            var attrs = new NSMutableDictionary();

            // Font
            var font = SpanToNSFont(span);
            attrs[NSStringAttributeKey.Font] = font;

            // TextColor
            if (span.TextColor != null)
                attrs[NSStringAttributeKey.ForegroundColor] = span.TextColor.ToPlatformColor();
            else if (label.TextColor != null)
                attrs[NSStringAttributeKey.ForegroundColor] = label.TextColor.ToPlatformColor();

            // BackgroundColor
            if (span.BackgroundColor != null)
                attrs[NSStringAttributeKey.BackgroundColor] = span.BackgroundColor.ToPlatformColor();

            // CharacterSpacing
            if (span.CharacterSpacing != 0)
                attrs[NSStringAttributeKey.KerningAdjustment] = NSNumber.FromDouble(span.CharacterSpacing);

            // TextDecorations
            if (span.TextDecorations.HasFlag(TextDecorations.Underline))
                attrs[NSStringAttributeKey.UnderlineStyle] = NSNumber.FromInt32((int)NSUnderlineStyle.Single);
            if (span.TextDecorations.HasFlag(TextDecorations.Strikethrough))
                attrs[NSStringAttributeKey.StrikethroughStyle] = NSNumber.FromInt32((int)NSUnderlineStyle.Single);

            // LineHeight
            if (span.LineHeight >= 0)
            {
                var paraStyle = new NSMutableParagraphStyle();
                paraStyle.LineHeightMultiple = (nfloat)span.LineHeight;
                attrs[NSStringAttributeKey.ParagraphStyle] = paraStyle;
            }

            var spanStr = new NSAttributedString(text, attrs);
            attributed.Append(spanStr);
        }

        handler.PlatformView.AttributedStringValue = attributed;
        handler.PlatformView.InvalidateIntrinsicContentSize();
    }
    static NSFont SpanToNSFont(Span span)
    {
        var size = span.FontSize > 0 ? (nfloat)span.FontSize : (nfloat)13.0;
        NSFont? nsFont = null;

        if (!string.IsNullOrEmpty(span.FontFamily))
            nsFont = NSFont.FromFontName(span.FontFamily, size);

        nsFont ??= NSFont.SystemFontOfSize(size);

        var manager = NSFontManager.SharedFontManager;
        if (span.FontAttributes.HasFlag(FontAttributes.Bold))
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Bold) ?? nsFont;
        if (span.FontAttributes.HasFlag(FontAttributes.Italic))
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Italic) ?? nsFont;

        return nsFont;
    }
}

internal static class ColorExtensions
{
    public static NSColor ToPlatformColor(this Graphics.Color color)
    {
        if (color == null)
            return NSColor.White;

        return NSColor.FromRgba(
            (nfloat)color.Red,
            (nfloat)color.Green,
            (nfloat)color.Blue,
            (nfloat)color.Alpha);
    }
}

internal static class FontExtensions
{
    public static NSFont ToNSFont(this Font font)
    {
        var size = font.Size > 0 ? (nfloat)font.Size : (nfloat)13.0;

        NSFont? nsFont = null;

        if (!string.IsNullOrEmpty(font.Family))
        {
            // Try the font manager (resolves registered font aliases like "FluentUI")
            var fontManager = IPlatformApplication.Current?.Services?.GetService(typeof(IFontManager)) as MacOSFontManager;
            if (fontManager != null)
                nsFont = fontManager.GetFont(font);
            else
                nsFont = NSFont.FromFontName(font.Family, size);
        }

        nsFont ??= NSFont.SystemFontOfSize(size);

        var manager = NSFontManager.SharedFontManager;
        if (font.Weight >= FontWeight.Bold)
            nsFont = manager.ConvertFont(nsFont, NSFontTraitMask.Bold) ?? nsFont;

        return nsFont;
    }
}
