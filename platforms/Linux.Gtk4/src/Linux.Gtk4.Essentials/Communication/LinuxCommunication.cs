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

    protected Process StartLauncher(string target)
    {
        var psi = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(target);

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start xdg-open.");
    }

    protected async Task WaitForLauncherAsync(Process process, CancellationToken ct = default)
    {
        using (process)
        {
            var stderr = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return;   // handler still running: treat as handed off
            }

            if (process.ExitCode != 0)
                throw new LauncherExitException(
                    process.ExitCode,
                    $"xdg-open failed with exit code {process.ExitCode} ({Describe(process.ExitCode)}): {(await stderr).Trim()}");

        }
    }

    // The awaited path used by Email and SMS stays the same for callers
    protected async Task LaunchAsync(string target, CancellationToken ct = default)
        => await WaitForLauncherAsync(StartLauncher(target), ct);


    static string Describe(int code) => code switch
    {
        1 => "bad syntax",
        2 => "file not found",
        3 => "required tool missing",
        4 => "action failed",
        _ => "unknown error"
    };
}
