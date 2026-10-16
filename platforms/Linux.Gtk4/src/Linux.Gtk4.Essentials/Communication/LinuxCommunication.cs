using System.Diagnostics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public sealed class LauncherExitException(int exitCode, string message)
    : InvalidOperationException(message)
{
    public int ExitCode { get; } = exitCode;
}

public class LinuxCommunication : LinuxScheme
{
    public LinuxCommunication(string protocol) : base(protocol)
    {
    }

    protected void Launch(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { uri },
            UseShellExecute = false,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start xdg-open.");

        var stderr = process.StandardError.ReadToEndAsync();

        // Still running after the timeout: a handler has taken over, treat as handed off
        if (!process.WaitForExit(TimeSpan.FromSeconds(2)))
            return;

        if (process.ExitCode != 0)
        {
            // A handler that inherited stderr can keep the pipe open, so don't wait for it forever
            var detail = stderr.Wait(TimeSpan.FromMilliseconds(500)) ? stderr.Result.Trim() : "";

            throw new LauncherExitException(
                process.ExitCode,
                $"xdg-open failed with exit code {process.ExitCode} ({Describe(process.ExitCode)}): {detail}");
        }
    }

    static string Describe(int code) => code switch
    {
        1 => "bad syntax",
        2 => "file not found",
        3 => "required tool missing",
        4 => "action failed",
        _ => "unknown error"
    };
}
