using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

/// <summary>
/// The .NET MAUI <see cref="Microsoft.Maui.Storage.SecureStorage"/> API is not supported on Linux.
/// All storage operations throw <see cref="PlatformNotSupportedException"/>.
/// </summary>
public class LinuxSecureStorage : ISecureStorage, IDisposable
{
	private const string UnsupportedMessage = "The .NET MAUI Microsoft.Maui.Storage.SecureStorage API is not supported on Linux.";

	/// <summary>
	/// Throws <see cref="PlatformNotSupportedException"/> because no storage backend is supported.
	/// </summary>
	public string Backend => throw new PlatformNotSupportedException(UnsupportedMessage);

	public Task<string?> GetAsync(string key)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public Task SetAsync(string key, string value)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public bool Remove(string key)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public void RemoveAll()
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public void Dispose()
	{
	}
}
