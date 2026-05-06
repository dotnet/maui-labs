using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Custom NSTextField subclass that supports text insets (padding).
/// Mirrors the iOS MauiLabel pattern of overriding DrawText/SizeThatFits,
/// but uses AppKit's NSTextFieldCell approach for inset drawing.
/// </summary>
public class MauiNSTextField : NSTextField
{
    public NSEdgeInsets TextInsets { get; set; }

    public MauiNSTextField()
    {
        Cell = new MauiNSTextFieldCell();
    }

    public override bool IsFlipped => true;

    /// <summary>
    /// Propagates insets to the cell before drawing/measuring.
    /// </summary>
    void SyncInsets()
    {
        if (Cell is MauiNSTextFieldCell cell)
            cell.TextInsets = TextInsets;
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        SyncInsets();
        base.DrawRect(dirtyRect);
    }

    public override CGSize IntrinsicContentSize
    {
        get
        {
            SyncInsets();
            var size = base.IntrinsicContentSize;
            // Guard against NaN — AppKit throws NSInvalidArgumentException
            if (nfloat.IsNaN(size.Width) || nfloat.IsNaN(size.Height))
                return size;
            // Add insets to intrinsic size (width may be -1 for wrapping labels)
            if (size.Width >= 0)
                size.Width += (nfloat)TextInsets.Left + (nfloat)TextInsets.Right;
            if (size.Height >= 0)
                size.Height += (nfloat)TextInsets.Top + (nfloat)TextInsets.Bottom;
            return size;
        }
    }
}

/// <summary>
/// Custom NSTextFieldCell that draws text inset by TextInsets.
/// This is the canonical AppKit approach for adding padding to NSTextField.
/// </summary>
internal class MauiNSTextFieldCell : NSTextFieldCell
{
    public NSEdgeInsets TextInsets { get; set; }

    public MauiNSTextFieldCell() : base(string.Empty)
    {
        Wraps = true;
    }

    CGRect InsetRect(CGRect rect)
    {
        return new CGRect(
            rect.X + (nfloat)TextInsets.Left,
            rect.Y + (nfloat)TextInsets.Top,
            rect.Width - (nfloat)TextInsets.Left - (nfloat)TextInsets.Right,
            rect.Height - (nfloat)TextInsets.Top - (nfloat)TextInsets.Bottom);
    }

    public override CGRect TitleRectForBounds(CGRect theRect)
    {
        return InsetRect(base.TitleRectForBounds(theRect));
    }

    public override void DrawInteriorWithFrame(CGRect cellFrame, NSView inView)
    {
        base.DrawInteriorWithFrame(InsetRect(cellFrame), inView);
    }

    public override void EditWithFrame(CGRect aRect, NSView inView, NSText editor, NSObject delegateObject, NSEvent theEvent)
    {
        base.EditWithFrame(InsetRect(aRect), inView, editor, delegateObject, theEvent);
    }

    public override void SelectWithFrame(CGRect aRect, NSView inView, NSText editor, NSObject delegateObject, nint selStart, nint selLength)
    {
        base.SelectWithFrame(InsetRect(aRect), inView, editor, delegateObject, selStart, selLength);
    }
}
