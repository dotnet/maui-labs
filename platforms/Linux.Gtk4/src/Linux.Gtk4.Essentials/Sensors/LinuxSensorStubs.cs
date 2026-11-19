using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Sensors;

public class LinuxGeocoding : IGeocoding
{
	public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude) =>
		throw new PlatformNotSupportedException("Geocoding is not available on Linux desktop.");

	public Task<IEnumerable<Location>> GetLocationsAsync(string address) =>
		throw new PlatformNotSupportedException("Geocoding is not available on Linux desktop.");
}
