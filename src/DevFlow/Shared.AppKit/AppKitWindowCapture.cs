#if MACOS
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class AppKitWindowCapture
{
    internal static byte[]? CaptureWindows(NSWindow parentWindow, NSWindow sheetWindow)
    {
        var windows = new[] { sheetWindow, parentWindow };
        var windowNumbers = windows
            .Where(window => window.WindowNumber > 0)
            .Select(window => new NSNumber((uint)window.WindowNumber))
            .ToArray();
        if (windowNumbers.Length != windows.Length)
        {
            foreach (var windowNumber in windowNumbers)
                windowNumber.Dispose();
            return null;
        }

        try
        {
            using var windowArray = NSArray.FromNSObjects(windowNumbers);
            var imageHandle = CGWindowListCreateImageFromArray(CGRect.Null, windowArray.Handle, 0x09);
            if (imageHandle != IntPtr.Zero)
            {
                using var image = Runtime.GetINativeObject<CGImage>(imageHandle, owns: true);
                if (image != null)
                {
                    using var bitmap = new NSBitmapImageRep(image);
                    return Encode(bitmap);
                }
            }

            return CaptureWindowPair(parentWindow, sheetWindow);
        }
        finally
        {
            foreach (var windowNumber in windowNumbers)
                windowNumber.Dispose();
        }
    }

    private static NSBitmapImageRep? CaptureBitmap(NSWindow window)
    {
        var imageHandle = CGWindowListCreateImage(CGRect.Null, 0x08, (uint)window.WindowNumber, 0x09);
        if (imageHandle == IntPtr.Zero)
            return null;

        using var image = Runtime.GetINativeObject<CGImage>(imageHandle, owns: true);
        return image == null ? null : new NSBitmapImageRep(image);
    }

    private static byte[]? CaptureWindowPair(NSWindow parentWindow, NSWindow sheetWindow)
    {
        using var parentBitmap = CaptureBitmap(parentWindow);
        using var sheetBitmap = CaptureBitmap(sheetWindow);
        if (parentBitmap == null || sheetBitmap == null)
            return null;

        var logicalSize = parentWindow.Frame.Size;
        using var composite = new NSBitmapImageRep(
            IntPtr.Zero,
            (int)parentBitmap.PixelsWide,
            (int)parentBitmap.PixelsHigh,
            8,
            4,
            true,
            false,
            NSColorSpace.DeviceRGB,
            0,
            0);
        composite.Size = logicalSize;

        using var parentImage = new NSImage(logicalSize);
        parentImage.AddRepresentation(parentBitmap);
        using var sheetImage = new NSImage(sheetWindow.Frame.Size);
        sheetImage.AddRepresentation(sheetBitmap);

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            using var context = NSGraphicsContext.FromBitmap(composite);
            if (context == null)
                return null;

            NSGraphicsContext.CurrentContext = context;
            parentImage.Draw(
                new CGRect(CGPoint.Empty, logicalSize),
                new CGRect(CGPoint.Empty, parentImage.Size),
                NSCompositingOperation.SourceOver,
                1,
                respectContextIsFlipped: false,
                hints: null);
            sheetImage.Draw(
                new CGRect(
                    sheetWindow.Frame.X - parentWindow.Frame.X,
                    sheetWindow.Frame.Y - parentWindow.Frame.Y,
                    sheetWindow.Frame.Width,
                    sheetWindow.Frame.Height),
                new CGRect(CGPoint.Empty, sheetImage.Size),
                NSCompositingOperation.SourceOver,
                1,
                respectContextIsFlipped: false,
                hints: null);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        return Encode(composite);
    }

    private static byte[]? Encode(NSBitmapImageRep bitmap)
    {
        using var properties = new NSDictionary();
        using var data = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, properties);
        return data?.ToArray();
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCreateImage(
        CGRect screenBounds, uint listOption, uint windowId, uint imageOption);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCreateImageFromArray(
        CGRect screenBounds, IntPtr windowArray, uint imageOption);
}
#endif
