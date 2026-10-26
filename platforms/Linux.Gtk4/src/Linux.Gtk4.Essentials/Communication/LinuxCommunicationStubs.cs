using Microsoft.Maui.ApplicationModel.Communication;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxContacts : IContacts
{
	public Task<Contact?> PickContactAsync() =>
		throw new PlatformNotSupportedException("Contacts are not available on Linux desktop.");

	public Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default) =>
		throw new PlatformNotSupportedException("Contacts are not available on Linux desktop.");
}
