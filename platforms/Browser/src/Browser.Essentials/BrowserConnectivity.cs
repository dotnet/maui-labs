using System.Runtime.Versioning;
using Microsoft.Maui.Networking;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Connectivity backed by navigator.onLine plus online/offline events and, where
/// available, the Network Information API for the connection profile.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserConnectivity : IConnectivity
{
	bool watching;

	public NetworkAccess NetworkAccess
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.IsOnline() ? NetworkAccess.Internet : NetworkAccess.None;
		}
	}

	public IEnumerable<ConnectionProfile> ConnectionProfiles
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			if (!BrowserEssentialsInterop.IsOnline())
				return [];

			// navigator.connection.type is only implemented on some platforms;
			// effectiveType ("4g" etc.) intentionally maps to Unknown.
			return BrowserEssentialsInterop.GetConnectionType() switch
			{
				"wifi" => [ConnectionProfile.WiFi],
				"cellular" => [ConnectionProfile.Cellular],
				"ethernet" => [ConnectionProfile.Ethernet],
				"bluetooth" => [ConnectionProfile.Bluetooth],
				_ => [ConnectionProfile.Unknown],
			};
		}
	}

	public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged
	{
		add
		{
			EnsureWatching();
			connectivityChanged += value;
		}
		remove => connectivityChanged -= value;
	}

	event EventHandler<ConnectivityChangedEventArgs>? connectivityChanged;

	void EnsureWatching()
	{
		BrowserEssentials.EnsureInitialized();
		if (watching)
			return;
		watching = true;
		BrowserEssentialsInterop.WatchConnectivity(_ =>
			connectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(NetworkAccess, ConnectionProfiles)));
	}
}
