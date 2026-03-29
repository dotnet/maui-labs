using Microsoft.Maui.Networking;

namespace Platform.Maui.Linux.Gtk4.Essentials.Networking;

public class LinuxConnectivity : IConnectivity
{
	private EventHandler<ConnectivityChangedEventArgs>? _connectivityChanged;

	public IEnumerable<ConnectionProfile> ConnectionProfiles => GetConnectionProfiles();

	public NetworkAccess NetworkAccess => GetNetworkAccess();

	public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged
	{
		add => _connectivityChanged += value;
		remove => _connectivityChanged -= value;
	}

	private static NetworkAccess GetNetworkAccess()
	{
		try
		{
			// Check for default route via /proc/net/route
			if (!File.Exists("/proc/net/route"))
				return NetworkAccess.Unknown;

			var lines = File.ReadAllLines("/proc/net/route");
			foreach (var line in lines.Skip(1)) // skip header
			{
				var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2 && parts[1] == "00000000")
					return NetworkAccess.Internet; // has default route
			}

			return NetworkAccess.Local;
		}
		catch { return NetworkAccess.Unknown; }
	}

	private static IEnumerable<ConnectionProfile> GetConnectionProfiles()
	{
		var profiles = new List<ConnectionProfile>();
		try
		{
			var netDir = "/sys/class/net";
			if (!Directory.Exists(netDir))
				return profiles;

			foreach (var dir in Directory.GetDirectories(netDir))
			{
				var ifName = Path.GetFileName(dir);
				if (ifName == "lo") continue;

				// Check if interface is up
				var operstatePath = Path.Combine(dir, "operstate");
				if (!File.Exists(operstatePath)) continue;
				var state = File.ReadAllText(operstatePath).Trim();
				if (state != "up") continue;

				// Determine type
				var typePath = Path.Combine(dir, "type");
				if (!File.Exists(typePath)) continue;
				var typeVal = File.ReadAllText(typePath).Trim();

				// Check if wireless (has /sys/class/net/<if>/wireless or /sys/class/net/<if>/phy80211)
				if (Directory.Exists(Path.Combine(dir, "wireless")) || Directory.Exists(Path.Combine(dir, "phy80211")))
					profiles.Add(ConnectionProfile.WiFi);
				else if (typeVal == "1") // Ethernet
					profiles.Add(ConnectionProfile.Ethernet);
				else
					profiles.Add(ConnectionProfile.Unknown);
			}
		}
		catch { }
		return profiles.Distinct();
	}
}
