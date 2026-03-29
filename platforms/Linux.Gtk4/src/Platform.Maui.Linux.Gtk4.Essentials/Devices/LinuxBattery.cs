using Microsoft.Maui.Devices;

namespace Platform.Maui.Linux.Gtk4.Essentials.Devices;

public class LinuxBattery : IBattery
{
	private EventHandler<BatteryInfoChangedEventArgs>? _batteryInfoChanged;
	private EventHandler<EnergySaverStatusChangedEventArgs>? _energySaverStatusChanged;

	public double ChargeLevel => ReadChargeLevel();
	public BatteryState State => ReadBatteryState();
	public BatteryPowerSource PowerSource => ReadPowerSource();
	public EnergySaverStatus EnergySaverStatus => EnergySaverStatus.Off;

	public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged
	{
		add => _batteryInfoChanged += value;
		remove => _batteryInfoChanged -= value;
	}

	public event EventHandler<EnergySaverStatusChangedEventArgs>? EnergySaverStatusChanged
	{
		add => _energySaverStatusChanged += value;
		remove => _energySaverStatusChanged -= value;
	}

	private static double ReadChargeLevel()
	{
		try
		{
			var path = FindBatteryPath("capacity");
			if (path is null) return 1.0;
			var value = File.ReadAllText(path).Trim();
			return int.TryParse(value, out var pct) ? pct / 100.0 : 1.0;
		}
		catch { return 1.0; }
	}

	private static BatteryState ReadBatteryState()
	{
		try
		{
			var path = FindBatteryPath("status");
			if (path is null) return BatteryState.NotPresent;
			var status = File.ReadAllText(path).Trim().ToLowerInvariant();
			return status switch
			{
				"charging" => BatteryState.Charging,
				"discharging" => BatteryState.Discharging,
				"full" => BatteryState.Full,
				"not charging" => BatteryState.NotCharging,
				_ => BatteryState.Unknown
			};
		}
		catch { return BatteryState.Unknown; }
	}

	private static BatteryPowerSource ReadPowerSource()
	{
		try
		{
			var state = ReadBatteryState();
			return state switch
			{
				BatteryState.Charging or BatteryState.Full => BatteryPowerSource.AC,
				BatteryState.Discharging => BatteryPowerSource.Battery,
				BatteryState.NotPresent => BatteryPowerSource.AC,
				_ => BatteryPowerSource.Unknown
			};
		}
		catch { return BatteryPowerSource.Unknown; }
	}

	private static string? FindBatteryPath(string file)
	{
		var baseDir = "/sys/class/power_supply";
		if (!Directory.Exists(baseDir)) return null;
		foreach (var dir in Directory.GetDirectories(baseDir))
		{
			var typePath = Path.Combine(dir, "type");
			if (File.Exists(typePath) && File.ReadAllText(typePath).Trim().Equals("Battery", StringComparison.OrdinalIgnoreCase))
			{
				var filePath = Path.Combine(dir, file);
				if (File.Exists(filePath))
					return filePath;
			}
		}
		return null;
	}
}
