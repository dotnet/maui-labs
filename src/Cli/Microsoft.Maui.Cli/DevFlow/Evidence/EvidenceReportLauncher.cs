using System.Diagnostics;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// Opens a generated report with the OS default handler. Only paths this tool just wrote are ever
/// passed here, and the file is launched by path (never through a shell command line), so a
/// bundle can never influence what gets executed.
/// </summary>
internal static class EvidenceReportLauncher
{
    public static bool TryOpen(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath)) return false;

        try
        {
            var full = Path.GetFullPath(reportPath);
            if (OperatingSystem.IsWindows())
            {
                using var shellLaunched = Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
                return true;
            }

            var launcher = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var info = new ProcessStartInfo(launcher) { UseShellExecute = false };
            info.ArgumentList.Add(full);
            using var launched = Process.Start(info);
            return launched is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
