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
}
