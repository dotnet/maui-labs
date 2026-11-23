using System.Diagnostics;
using System.IO;

namespace UITests.Helpers;

/// <summary>
/// Launches and manages the WPF ControlGallery app for testing.
/// </summary>
public class AppLauncher : IDisposable
{
    // Optional overrides so the suite can run against an externally provisioned gallery
    // (for example one built for net10.0 or net11.0 against locally packed WPF packages).
    const string GalleryExeEnvVar = "MAUI_WPF_UITESTS_GALLERY_EXE";
    const string GalleryProjectEnvVar = "MAUI_WPF_UITESTS_GALLERY_PROJECT";
    const string ScreenshotDirEnvVar = "MAUI_WPF_UITESTS_SCREENSHOT_DIR";
    const string WpfBackendModuleName = "Microsoft.Maui.Platforms.Windows.WPF.dll";

    Process? _dotnetProcess;
    readonly string _wpfProjectPath;
    readonly string? _wpfExePath;
    readonly string? _winuiExePath;
    readonly string _screenshotDir;

    public AppLauncher(string? wpfProjectPath = null, string? winuiExePath = null)
    {
        var repoRoot = FindRepoRoot();
        _wpfExePath = Environment.GetEnvironmentVariable(GalleryExeEnvVar) is { Length: > 0 } exe ? exe : null;
        _wpfProjectPath = wpfProjectPath
            ?? (Environment.GetEnvironmentVariable(GalleryProjectEnvVar) is { Length: > 0 } project ? project : null)
            ?? Path.Combine(repoRoot, "samples", "ControlGallery", "ControlGallery.csproj");
        _winuiExePath = winuiExePath
            ?? @"D:\repos\davidortinau\ControlGallery\src\ControlGallery\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ControlGallery.exe";
        _screenshotDir = Environment.GetEnvironmentVariable(ScreenshotDirEnvVar) is { Length: > 0 } screenshots
            ? screenshots
            : Path.Combine(repoRoot, "platforms", "Windows.WPF", "tests", "UITests", "Screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    /// <summary>
    /// True when the process hosts the MAUI WPF backend, independent of its target framework,
    /// so net10.0 and net11.0 galleries are both recognized and WinUI galleries are skipped.
    /// </summary>
    public bool IsWpfGalleryProcess(Process p)
    {
        try
        {
            if (_wpfExePath != null &&
                !string.Equals(p.MainModule?.FileName, Path.GetFullPath(_wpfExePath), StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (ProcessModule module in p.Modules)
            {
                if (string.Equals(module.ModuleName, WpfBackendModuleName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not inspect gallery process {p.Id}: {ex.Message}");
        }
        return false;
    }

    public string ScreenshotDir => _screenshotDir;

    static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "MauiLabs.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate the maui-labs repository root.");
    }

    public Process LaunchWpf(bool build = false)
    {
        // Check if already running
        var existing = FindWpfProcess();
        if (existing != null) return existing;

        if (_wpfExePath != null)
        {
            if (!File.Exists(_wpfExePath))
                throw new FileNotFoundException($"{GalleryExeEnvVar} points to a missing file", _wpfExePath);

            _dotnetProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _wpfExePath,
                WorkingDirectory = Path.GetDirectoryName(_wpfExePath),
                UseShellExecute = false,
            });

            return WaitForWpfProcess(TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("WPF ControlGallery process did not create a window within timeout");
        }

        if (!File.Exists(_wpfProjectPath))
            throw new FileNotFoundException(
                $"Provision ControlGallery and set {GalleryExeEnvVar} or {GalleryProjectEnvVar}.", _wpfProjectPath);

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
    Process? FindWpfProcess()
    {
        foreach (var p in Process.GetProcessesByName("ControlGallery"))
        {
            try
            {
                p.Refresh();
                if (p.MainWindowHandle == IntPtr.Zero)
                    continue;
                if (!IsWpfGalleryProcess(p))
                    continue;
                return p;
            }
            catch { }
        }
        return null;
    }

    Process? WaitForWpfProcess(TimeSpan timeout)
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
