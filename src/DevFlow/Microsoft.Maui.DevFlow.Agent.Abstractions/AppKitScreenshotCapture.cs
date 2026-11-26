namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class AppKitScreenshotCapture
{
    internal static async Task<byte[]?> CaptureAsync(
        bool hasAttachedSheet,
        Func<Task<byte[]?>> captureScreenCaptureKit,
        Func<byte[]?> captureComposite,
        Func<int, int, bool>? isMacOSVersionAtLeast = null)
    {
        isMacOSVersionAtLeast ??= (major, minor) => OperatingSystem.IsMacOSVersionAtLeast(major, minor);
        // Before macOS 14.2, a successful ScreenCaptureKit image still omits attached sheets.
        if (!hasAttachedSheet || isMacOSVersionAtLeast(14, 2))
        {
            var image = await captureScreenCaptureKit();
            if (image != null)
                return image;
        }

        return hasAttachedSheet ? captureComposite() : null;
    }

    /// <summary>
    /// Returns true when every RGBA pixel matches the first pixel within <paramref name="tolerance"/>
    /// per channel, i.e. the capture contains no visible content (for example an all-white
    /// image from a window whose web content stopped painting while covered).
    /// </summary>
    internal static bool IsUniformRgba(ReadOnlySpan<byte> rgba, int tolerance = 2)
    {
        if (rgba.Length < 8)
            return false;

        var r = rgba[0];
        var g = rgba[1];
        var b = rgba[2];
        var a = rgba[3];
        for (var i = 4; i + 3 < rgba.Length; i += 4)
        {
            if (Math.Abs(rgba[i] - r) > tolerance ||
                Math.Abs(rgba[i + 1] - g) > tolerance ||
                Math.Abs(rgba[i + 2] - b) > tolerance ||
                Math.Abs(rgba[i + 3] - a) > tolerance)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps a window's content layout rect (window coordinates, bottom-left origin, as returned by
    /// <c>NSWindow.ContentLayoutRect</c>) onto a captured image of the whole window (top-left
    /// origin). Used so the title bar and toolbar do not hide a blank content area. Returns null
    /// when the inputs cannot describe a non-empty region.
    /// </summary>
    internal static (int X, int Y, int Width, int Height)? GetContentPixelRect(
        double windowWidth, double windowHeight,
        double contentX, double contentY, double contentWidth, double contentHeight,
        int imageWidth, int imageHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0 || imageWidth <= 0 || imageHeight <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
            return null;

        var scaleX = imageWidth / windowWidth;
        var scaleY = imageHeight / windowHeight;
        var left = Math.Clamp((int)Math.Ceiling(contentX * scaleX), 0, imageWidth);
        var right = Math.Clamp((int)Math.Floor((contentX + contentWidth) * scaleX), 0, imageWidth);
        var top = Math.Clamp((int)Math.Ceiling((windowHeight - (contentY + contentHeight)) * scaleY), 0, imageHeight);
        var bottom = Math.Clamp((int)Math.Floor((windowHeight - contentY) * scaleY), 0, imageHeight);

        return right > left && bottom > top
            ? (left, top, right - left, bottom - top)
            : null;
    }
}
