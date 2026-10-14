using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class ScreenshotOperations(AgentClient client)
{
    internal async Task<ScreenshotResult> CaptureAsync(int? window, string? id, string? selector, int? maxWidth, string? scale)
    {
        byte[]? data = null;

        // The simulator capture also includes system dialogs outside the app's visual tree.
        if (id == null && selector == null && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            var status = await client.GetStatusAsync();
            if (status?.Platform?.Contains("iOS", StringComparison.OrdinalIgnoreCase) == true)
                data = await TrySimctlScreenshotAsync();
        }

        if (data is null)
            return await client.ScreenshotResultAsync(window, id, selector, maxWidth, scale);

        return new ScreenshotResult { Success = true, Data = ResizeSimctlScreenshot(data, maxWidth, scale) };
    }

    private static async Task<byte[]?> TrySimctlScreenshotAsync()
    {
        try
        {
            var udid = await HostOperations.ResolveUdidAsync(null);

            var file = Path.Combine(Path.GetTempPath(), $"devflow-simctl-{Guid.NewGuid():N}.png");
            try
            {
                var result = await ProcessRunner.RunAsync("xcrun", ["simctl", "io", udid, "screenshot", "--type", "png", file]);

                if (result.ExitCode == 0 && File.Exists(file))
                {
                    var bytes = await File.ReadAllBytesAsync(file);
                    if (bytes.Length > 0)
                        return bytes;
                }
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Preserve the simulator probe fallback to the agent capture.
        }
        return null;
    }

    internal static byte[] ResizeSimctlScreenshot(byte[] data, int? maxWidth, string? scale)
    {
        if (scale != null && (scale.Equals("native", StringComparison.OrdinalIgnoreCase)
            || scale.Equals("full", StringComparison.OrdinalIgnoreCase)))
            return data;

        try
        {
            using var original = SkiaSharp.SKBitmap.Decode(data);
            if (original == null) return data;

            // Infer the simulator density so its native capture matches the agent's logical pixels.
            int? targetWidth = maxWidth;
            if (targetWidth == null)
            {
                double density = original.Width switch
                {
                    1290 or 1320 or 1206 => 3.0, // iPhone 14/15/16 Pro, Pro Max, standard
                    1170 => 3.0,                   // iPhone 12/13/14
                    1125 => 3.0,                   // iPhone X/XS/11 Pro
                    1242 => 3.0,                   // iPhone 8+/XS Max
                    828 => 2.0,                    // iPhone XR/11
                    750 => 2.0,                    // iPhone 8/SE
                    2048 or 2388 or 2360 => 2.0,   // iPad Pro/Air
                    _ => original.Width > 1000 ? 3.0 : 2.0 // default: assume 3x for large, 2x otherwise
                };
                targetWidth = (int)(original.Width / density);
            }

            if (targetWidth <= 0 || targetWidth >= original.Width)
                return data;

            var ratio = (float)targetWidth.Value / original.Width;
            var newHeight = (int)(original.Height * ratio);

            using var resized = original.Resize(
                new SkiaSharp.SKImageInfo(targetWidth.Value, newHeight),
                SkiaSharp.SKSamplingOptions.Default);
            if (resized == null) return data;

            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        catch
        {
            return data;
        }
    }
}
