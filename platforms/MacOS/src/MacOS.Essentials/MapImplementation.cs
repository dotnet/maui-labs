using AppKit;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class MapImplementation : IMap
{
	public Task OpenAsync(double latitude, double longitude, MapLaunchOptions options)
	{
		var url = BuildAppleMapsUrl(latitude, longitude, options);
		NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		return Task.CompletedTask;
	}

	public Task OpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		var address = BuildAddress(placemark);
		var url = $"https://maps.apple.com/?address={Uri.EscapeDataString(address)}";
		if (!string.IsNullOrEmpty(options.Name))
			url += $"&q={Uri.EscapeDataString(options.Name)}";
		NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		return Task.CompletedTask;
	}

	public Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options)
	{
		var url = BuildAppleMapsUrl(latitude, longitude, options);
		var result = NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		return Task.FromResult(result);
	}

	public Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		var address = BuildAddress(placemark);
		var url = $"https://maps.apple.com/?address={Uri.EscapeDataString(address)}";
		if (!string.IsNullOrEmpty(options.Name))
			url += $"&q={Uri.EscapeDataString(options.Name)}";
		var result = NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		return Task.FromResult(result);
	}

	static string BuildAppleMapsUrl(double latitude, double longitude, MapLaunchOptions options)
	{
		var url = $"https://maps.apple.com/?ll={latitude},{longitude}";
		if (!string.IsNullOrEmpty(options.Name))
			url += $"&q={Uri.EscapeDataString(options.Name)}";
		return url;
	}

	static string BuildAddress(Placemark placemark)
	{
		var parts = new List<string>();
		if (!string.IsNullOrEmpty(placemark.Thoroughfare)) parts.Add(placemark.Thoroughfare);
		if (!string.IsNullOrEmpty(placemark.Locality)) parts.Add(placemark.Locality);
		if (!string.IsNullOrEmpty(placemark.AdminArea)) parts.Add(placemark.AdminArea);
		if (!string.IsNullOrEmpty(placemark.PostalCode)) parts.Add(placemark.PostalCode);
		if (!string.IsNullOrEmpty(placemark.CountryName)) parts.Add(placemark.CountryName);
		return string.Join(", ", parts);
	}
}
