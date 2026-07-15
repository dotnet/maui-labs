using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Battery info backed by navigator.getBattery() (Battery Status API — Chromium only).
/// Where the API is unavailable the state reads as Unknown with a full charge level.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserBattery : IBattery
{
	Task? startTask;
	double chargeLevel = 1.0;
	BatteryState state = BatteryState.Unknown;
	BatteryPowerSource powerSource = BatteryPowerSource.Unknown;

	public double ChargeLevel
	{
		get
		{
			EnsureStarted();
			return chargeLevel;
		}
	}

	public BatteryState State
	{
		get
		{
			EnsureStarted();
			return state;
		}
	}

	public BatteryPowerSource PowerSource
	{
		get
		{
			EnsureStarted();
			return powerSource;
		}
	}

	public EnergySaverStatus EnergySaverStatus => EnergySaverStatus.Unknown;

	public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged;

	public event EventHandler<EnergySaverStatusChangedEventArgs>? EnergySaverStatusChanged
	{
		add { } // Browsers expose no energy-saver signal; the event never fires.
		remove { }
	}

	void EnsureStarted()
	{
		BrowserEssentials.EnsureInitialized();
		startTask ??= StartAsync();
	}

	async Task StartAsync()
	{
		var initial = await BrowserEssentialsInterop.BatteryStart(json =>
		{
			Apply(json);
			BatteryInfoChanged?.Invoke(this, new BatteryInfoChangedEventArgs(chargeLevel, state, powerSource));
		}).ConfigureAwait(false);
		if (initial is not null)
			Apply(initial);
	}

	void Apply(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		chargeLevel = root.GetProperty("level").GetDouble();
		var charging = root.GetProperty("charging").GetBoolean();
		state = charging
			? (chargeLevel >= 1.0 ? BatteryState.Full : BatteryState.Charging)
			: BatteryState.Discharging;
		powerSource = charging ? BatteryPowerSource.AC : BatteryPowerSource.Battery;
	}
}
