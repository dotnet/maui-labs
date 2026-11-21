using Microsoft.Maui.Devices;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public sealed class BatteryTests
{
	[Theory]
	[InlineData(1, 128, 80, -1.0, BatteryState.NotPresent, BatteryPowerSource.AC)]
	[InlineData(255, 255, 255, -1.0, BatteryState.Unknown, BatteryPowerSource.Unknown)]
	[InlineData(1, 1, 100, 1.0, BatteryState.Full, BatteryPowerSource.AC)]
	[InlineData(1, 8, 42, 0.42, BatteryState.Charging, BatteryPowerSource.AC)]
	[InlineData(0, 2, 30, 0.3, BatteryState.Discharging, BatteryPowerSource.Battery)]
	[InlineData(0, 0, 255, -1.0, BatteryState.Discharging, BatteryPowerSource.Battery)]
	public void NativeStatus_MapsWithoutSyntheticCharge(
		byte ac,
		byte flag,
		byte percent,
		double chargeLevel,
		BatteryState state,
		BatteryPowerSource source)
	{
		var snapshot = WPFBattery.MapSnapshot(
			new WPFBattery.SystemPowerStatus
			{
				ACLineStatus = ac,
				BatteryFlag = flag,
				BatteryLifePercent = percent,
			});

		Assert.Equal(chargeLevel, snapshot.ChargeLevel);
		Assert.Equal(state, snapshot.State);
		Assert.Equal(source, snapshot.PowerSource);
	}

	[Fact]
	public void PollingFailure_IsPublishedWithoutEscapingTimerCallback()
	{
		var calls = 0;
		var expected = new InvalidOperationException("power API failed");
		using var battery = new WPFBattery(
			() => calls++ == 0
				? new WPFBattery.PowerSnapshot(
					0.5,
					BatteryState.Discharging,
					BatteryPowerSource.Battery,
					EnergySaverStatus.Off)
				: throw expected,
			TimeSpan.FromHours(1));
		Exception? received = null;
		battery.PollingFailed += (_, error) => received = error;

		var error = Record.Exception(battery.PollOnce);

		Assert.Null(error);
		Assert.Same(expected, received);
	}

	[Fact]
	public void SubscriberFailure_IsPublishedWithoutEscapingTimerCallback()
	{
		var snapshots = new Queue<WPFBattery.PowerSnapshot>(
		[
			new(0.5, BatteryState.Discharging, BatteryPowerSource.Battery, EnergySaverStatus.Off),
			new(0.5, BatteryState.Discharging, BatteryPowerSource.Battery, EnergySaverStatus.Off),
			new(0.4, BatteryState.Discharging, BatteryPowerSource.Battery, EnergySaverStatus.Off),
		]);
		using var battery = new WPFBattery(
			() => snapshots.Dequeue(),
			TimeSpan.FromHours(1));
		var expected = new InvalidOperationException("subscriber failed");
		Exception? received = null;
		battery.PollingFailed += (_, error) => received = error;
		battery.BatteryInfoChanged += (_, _) => throw expected;

		var error = Record.Exception(battery.PollOnce);

		Assert.Null(error);
		Assert.Same(expected, received);
	}

}
