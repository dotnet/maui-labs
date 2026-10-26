using Microsoft.Maui.ApplicationModel.Communication;

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

        Launch("tel:" + PhoneNumberNormalizer.Normalize(number, nameof(number)));
    }
}