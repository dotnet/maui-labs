using System.Diagnostics;
using System.IO;

namespace UITests.Helpers;

/// <summary>
/// Launches and manages the WPF ControlGallery app for testing.
/// </summary>
public class AppLauncher : IDisposable
{
    Process? _dotnetProcess;
    readonly string _wpfProjectPath;
    readonly string? _winuiExePath;
    readonly string _screenshotDir;

    public AppLauncher(string? wpfProjectPath = null, string? winuiExePath = null)
    {
        var repoRoot = FindRepoRoot();
        _wpfProjectPath = wpfProjectPath
            ?? Path.Combine(repoRoot, "samples", "ControlGallery", "ControlGallery.csproj");
        _winuiExePath = winuiExePath
            ?? @"D:\repos\davidortinau\ControlGallery\src\ControlGallery\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ControlGallery.exe";
        _screenshotDir = Path.Combine(repoRoot, "tests", "UITests", "Screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    public string ScreenshotDir => _screenshotDir;

    static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Maui.WPF.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return @"D:\repos\rmarinho\maui.wpf";
    }

    public Process LaunchWpf(bool build = false)
    {
        // Check if already running
        var existing = FindWpfProcess();
        if (existing != null) return existing;

        if (build)
        {
            var buildProc = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{_wpfProjectPath}\" --no-restore -v:q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (buildProc != null)
            {
                buildProc.WaitForExit(120_000);
                try
                {
                    if (buildProc.ExitCode != 0)
                        throw new Exception($"Build failed with exit code {buildProc.ExitCode}");
                }
                catch (InvalidOperationException)
                {
                    // Process state issue — ignore, just continue
                }
            }
        }

        _dotnetProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_wpfProjectPath}\" --no-build",
            UseShellExecute = false,
        });

        // Wait for the actual ControlGallery window to appear
        var proc = WaitForWpfProcess(TimeSpan.FromSeconds(20));
        if (proc == null)
            throw new TimeoutException("WPF ControlGallery process did not create a window within timeout");
        return proc;
    }

    /// <summary>
    /// Find the ControlGallery process with a visible main window (the WPF one).
    /// dotnet run spawns a child process, so we look for ControlGallery by name.
    /// </summary>
    static Process? FindWpfProcess()
    {
        foreach (var p in Process.GetProcessesByName("ControlGallery"))
        {
            try
            {
                p.Refresh();
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle))
                    continue;
                // Skip WinUI processes (different TFM path)
                var path = p.MainModule?.FileName ?? "";
                if (path.Contains("net10.0-windows10.0", StringComparison.OrdinalIgnoreCase))
                    continue;
                return p;
            }
            catch { }
        }
        return null;
    }

    static Process? WaitForWpfProcess(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var proc = FindWpfProcess();
            if (proc != null) return proc;
            Thread.Sleep(500);
        }
        return null;
    }

    public Process? LaunchWinUI()
    {
        if (_winuiExePath == null || !File.Exists(_winuiExePath))
            return null;

        // Kill any existing WinUI instances to avoid confusion
        var existing = Process.GetProcessesByName("ControlGallery");
        var wpfProc = FindWpfProcess();

        var winuiProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _winuiExePath,
            UseShellExecute = false,
        });

        if (winuiProcess == null) return null;

        // Wait for a NEW ControlGallery window (not the WPF one)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var p in Process.GetProcessesByName("ControlGallery"))
            {
                try
                {
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero && wpfProc != null && p.Id != wpfProc.Id)
                        return p;
                    if (p.MainWindowHandle != IntPtr.Zero && wpfProc == null)
                        return p;
                }
                catch { }
            }
            Thread.Sleep(500);
        }
        return null;
    }

    public void Dispose()
    {
        // Kill the dotnet run process tree
        TryKill(_dotnetProcess);
        // Also kill any remaining ControlGallery processes we spawned
    }

    static void TryKill(Process? p)
    {
        if (p == null) return;
        try
        {
            if (!p.HasExited)
                p.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
