using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxMap : IMap
{
	public async Task OpenAsync(double latitude, double longitude, MapLaunchOptions options)
	{
		var url = $"https://www.openstreetmap.org/?mlat={latitude.ToString(CultureInfo.InvariantCulture)}&mlon={longitude.ToString(CultureInfo.InvariantCulture)}&zoom=15";
		await LaunchUrl(url);
	}

	public async Task OpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		ArgumentNullException.ThrowIfNull(placemark);
		var address = Uri.EscapeDataString(
			$"{placemark.Thoroughfare} {placemark.Locality} {placemark.AdminArea} {placemark.CountryName}".Trim());
		var url = $"https://www.openstreetmap.org/search?query={address}";
		await LaunchUrl(url);
	}

	public async Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options)
	{
		try { await OpenAsync(latitude, longitude, options); return true; }
		catch { return false; }
	}

	public async Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		try { await OpenAsync(placemark, options); return true; }
		catch { return false; }
	}

	private static Task LaunchUrl(string url)
	{
		var psi = new ProcessStartInfo("xdg-open", url)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		Process.Start(psi);
		return Task.CompletedTask;
	}
}
