using System.Diagnostics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxCommunication : LinuxScheme
{
    public LinuxCommunication(string protocol) : base(protocol)
    {
    }

    protected async Task LaunchAsync(string target, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(target);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start xdg-open.");

        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return;   // still running: the handler took over, treat as success
        }

#if DEBUG
        if (process.ExitCode != 0)
            Debug.WriteLine($"xdg-open failed with exit code {process.ExitCode} ({Describe(process.ExitCode)}): {(await stderr).Trim()}");
#endif
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
