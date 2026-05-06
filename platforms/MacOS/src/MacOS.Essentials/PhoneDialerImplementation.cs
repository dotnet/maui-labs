using Microsoft.Maui.ApplicationModel.Communication;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class PhoneDialerImplementation : IPhoneDialer
{
	public bool IsSupported => false;

	public void Open(string number)
		=> throw new NotSupportedException("Phone dialer is not available on macOS.");
}
