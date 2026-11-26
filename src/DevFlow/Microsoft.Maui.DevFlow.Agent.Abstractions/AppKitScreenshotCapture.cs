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
}
