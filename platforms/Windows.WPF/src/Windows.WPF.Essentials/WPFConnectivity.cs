using System.Net.NetworkInformation;
using Microsoft.Maui.Networking;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFConnectivity : IConnectivity, IDisposable
	{
		readonly NetworkAvailabilityChangedEventHandler _handler;

		public WPFConnectivity()
		{
			_handler = (s, e) =>
				ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(NetworkAccess, ConnectionProfiles));
			NetworkChange.NetworkAvailabilityChanged += _handler;
		}

		public NetworkAccess NetworkAccess =>
			NetworkInterface.GetIsNetworkAvailable() ? NetworkAccess.Internet : NetworkAccess.None;

		public IEnumerable<ConnectionProfile> ConnectionProfiles
		{
			get
			{
				var profiles = new List<ConnectionProfile>();
				foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
				{
					if (ni.OperationalStatus != OperationalStatus.Up) continue;
					switch (ni.NetworkInterfaceType)
					{
						case NetworkInterfaceType.Wireless80211:
							profiles.Add(ConnectionProfile.WiFi);
							break;
						case NetworkInterfaceType.Ethernet:
							profiles.Add(ConnectionProfile.Ethernet);
							break;
					}
				}
				return profiles.Distinct();
			}
		}

		public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

		public void Dispose()
		{
			NetworkChange.NetworkAvailabilityChanged -= _handler;
		}
	}
}
