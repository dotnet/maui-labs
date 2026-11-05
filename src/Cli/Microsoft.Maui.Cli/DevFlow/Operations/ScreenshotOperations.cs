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
                data = await TrySimctlScreenshotAsync(maxWidth, scale);
        }

        if (data is null)
            return await client.ScreenshotResultAsync(window, id, selector, maxWidth, scale);

        return new ScreenshotResult { Success = true, Data = data };
    }

    private static async Task<byte[]?> TrySimctlScreenshotAsync(int? maxWidth, string? scale)
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
                    var bytes = await ScreenshotImage.ResizeSimulatorAsync(file, maxWidth, scale);
                    if (bytes.Length > 0)
                        return bytes;
                }
            }
            finally
            {
                ScreenshotImage.DeleteTemporaryFile(file);
            }
        }
        catch
        {
            // Preserve the simulator probe fallback to the agent capture.
        }
        return null;
    }
}
