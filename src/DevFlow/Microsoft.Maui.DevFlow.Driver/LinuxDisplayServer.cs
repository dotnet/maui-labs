using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Detects the Linux display server (X11 vs Wayland) and available input automation tools.
/// </summary>
internal static class LinuxDisplayServer
{
    /// <summary>
    /// Returns true if the current session is running on Wayland.
    /// </summary>
    public static bool IsWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null;

    /// <summary>
    /// Returns true if the current session is running on X11.
    /// Falls back to true if the session type cannot be determined (legacy default).
    /// </summary>
    public static bool IsX11 => !IsWayland;

    /// <summary>
    /// Checks whether a command-line tool is available on PATH.
    /// </summary>
    public static bool IsToolAvailable(string toolName)
    {
        try
        {
            var psi = new ProcessStartInfo("which", toolName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            if (!proc.WaitForExit(3000))
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the ydotoold daemon is running (required for ydotool).
    /// </summary>
    public static bool IsYdotooldRunning()
    {
        try
        {
            return Process.GetProcessesByName("ydotoold").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the recommended input tool for the current session.
    /// Prefers xdotool on X11, ydotool on Wayland. Falls back to whichever is available.
    /// </summary>
    public static InputTool GetPreferredInputTool()
    {
        var hasXdotool = IsToolAvailable("xdotool");
        var hasYdotool = IsToolAvailable("ydotool");

        if (IsWayland)
        {
            if (hasYdotool) return InputTool.Ydotool;
            if (hasXdotool) return InputTool.Xdotool; // XWayland fallback
            return InputTool.None;
        }

        // X11
        if (hasXdotool) return InputTool.Xdotool;
        if (hasYdotool) return InputTool.Ydotool;
        return InputTool.None;
    }

    /// <summary>
    /// Returns the recommended ffmpeg capture format for the current display server.
    /// </summary>
    public static string GetFfmpegCaptureFormat() => IsWayland ? "pipewire" : "x11grab";

    /// <summary>
    /// Returns the default capture display identifier for the current session.
    /// For X11, returns the DISPLAY value (e.g. ":0"). For Wayland/PipeWire, returns "0".
    /// </summary>
    public static string GetDefaultCaptureDisplay()
    {
        if (IsWayland)
            return "0"; // PipeWire default screen source

        var display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        return display;
    }
}

internal enum InputTool
{
    None,
    Xdotool,
    Ydotool
}
