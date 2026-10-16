using Microsoft.Maui.ApplicationModel.Communication;
using System.Diagnostics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxPhoneDialer : LinuxCommunication, IPhoneDialer
{
    public LinuxPhoneDialer() : base("tel")
    {
    }

    public bool IsSupported => HasHandler;

    public void Open(string number)
    {
        if (!IsSupported)
            throw new FeatureNotSupportedException("Phone dialer is not available on this Linux system.");

        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var process = StartLauncher("tel:" + PhoneNumberNormalizer.Normalize(number, nameof(number)));
        _ = ObserveAsync(WaitForLauncherAsync(process));
    }

    static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) { Trace.TraceError($"PhoneDialer: {ex.Message}"); }
    }
}