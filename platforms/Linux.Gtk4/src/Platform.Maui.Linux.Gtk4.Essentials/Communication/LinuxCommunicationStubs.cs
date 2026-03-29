using Microsoft.Maui.ApplicationModel.Communication;

namespace Platform.Maui.Linux.Gtk4.Essentials.Communication;

public class LinuxPhoneDialer : IPhoneDialer
{
	public bool IsSupported => false;
	public void Open(string number) =>
		throw new PlatformNotSupportedException("Phone dialer is not available on Linux desktop.");
}

public class LinuxSms : ISms
{
	public bool IsComposeSupported => false;
	public Task ComposeAsync(SmsMessage? message) =>
		throw new PlatformNotSupportedException("SMS is not available on Linux desktop.");
}

public class LinuxContacts : IContacts
{
	public Task<Contact?> PickContactAsync() =>
		throw new PlatformNotSupportedException("Contacts are not available on Linux desktop.");

	public Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default) =>
		throw new PlatformNotSupportedException("Contacts are not available on Linux desktop.");
}
