using System.Runtime.InteropServices;
using Microsoft.Maui.Devices;

namespace Platform.Maui.Linux.Gtk4.Essentials.Devices;

public class LinuxDeviceInfo : IDeviceInfo
{
	private readonly Lazy<(string model, string manufacturer)> _hwInfo = new(ReadHardwareInfo);

	public string Model => _hwInfo.Value.model;
	public string Manufacturer => _hwInfo.Value.manufacturer;
	public string Name => Environment.MachineName;
	public string VersionString => Environment.OSVersion.VersionString;
	public Version Version => Environment.OSVersion.Version;
	public DevicePlatform Platform => DevicePlatform.Create("Linux");
	public DeviceIdiom Idiom => DeviceIdiom.Desktop;
	public DeviceType DeviceType => DeviceType.Physical;

	private static (string model, string manufacturer) ReadHardwareInfo()
	{
		var model = ReadFileOrDefault("/sys/devices/virtual/dmi/id/product_name", "Linux Device");
		var manufacturer = ReadFileOrDefault("/sys/devices/virtual/dmi/id/sys_vendor", "Unknown");
		return (model.Trim(), manufacturer.Trim());
	}

	private static string ReadFileOrDefault(string path, string defaultValue)
	{
		try { return File.ReadAllText(path).Trim(); }
		catch { return defaultValue; }
	}
}
