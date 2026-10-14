using System.Diagnostics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxCommunication : LinuxScheme
{
    public LinuxCommunication(string protocol) : base(protocol)
    {
    }

    protected void Launch(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { uri },
            UseShellExecute = false,
            CreateNoWindow = true

        })?.Dispose();
    }
}
