using System.Diagnostics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxScheme
{
    public LinuxScheme(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        _hasHandler = new Lazy<bool>(() => HasProtocolHandler(protocol));
    }

    readonly Lazy<bool> _hasHandler;
    public bool HasHandler => _hasHandler.Value;

    bool HasProtocolHandler(string protocol)
    {
        if (protocol is "sms" or "tel")
            protocol = $"x-scheme-handler/{protocol}";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    ArgumentList = { "query", "default", protocol },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return false;

            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }
}
