using System.Runtime.InteropServices;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFBattery : IBattery, IDisposable
{
	readonly object _gate = new();
	readonly Func<PowerSnapshot> _readSnapshot;
	readonly TimeSpan _pollingInterval;
	Timer? _timer;
	PowerSnapshot _snapshot;
	EventHandler<BatteryInfoChangedEventArgs>? _batteryInfoChanged;
	EventHandler<EnergySaverStatusChangedEventArgs>? _energySaverStatusChanged;

	public WPFBattery()
		: this(ReadSnapshot, TimeSpan.FromSeconds(5))
	{
	}

	internal WPFBattery(
		Func<PowerSnapshot> readSnapshot,
		TimeSpan pollingInterval)
	{
		_readSnapshot = readSnapshot;
		_pollingInterval = pollingInterval;
		_snapshot = _readSnapshot();
	}

	public double ChargeLevel => _readSnapshot().ChargeLevel;

	public BatteryState State => _readSnapshot().State;

	public BatteryPowerSource PowerSource => _readSnapshot().PowerSource;

	public EnergySaverStatus EnergySaverStatus => _readSnapshot().EnergySaverStatus;

	internal event EventHandler<Exception>? PollingFailed;

	public event EventHandler<BatteryInfoChangedEventArgs> BatteryInfoChanged
	{
		add
		{
			lock (_gate)
			{
				_batteryInfoChanged += value;
				EnsurePolling();
			}
		}
		remove
		{
			lock (_gate)
			{
				_batteryInfoChanged -= value;
				StopPollingIfUnused();
			}
		}
	}

	public event EventHandler<EnergySaverStatusChangedEventArgs> EnergySaverStatusChanged
	{
		add
		{
			lock (_gate)
			{
				_energySaverStatusChanged += value;
				EnsurePolling();
			}
		}
		remove
		{
			lock (_gate)
			{
				_energySaverStatusChanged -= value;
				StopPollingIfUnused();
			}
		}
	}

	void EnsurePolling()
	{
		_snapshot = _readSnapshot();
		_timer ??= new Timer(Poll, null, _pollingInterval, _pollingInterval);
	}

	void StopPollingIfUnused()
	{
		if (_batteryInfoChanged is not null || _energySaverStatusChanged is not null)
			return;

		_timer?.Dispose();
		_timer = null;
	}

	void Poll(object? state)
	{
		PollOnce();
	}

	internal void PollOnce()
	{
		PowerSnapshot current;
		try
		{
			current = _readSnapshot();
		}
		catch (Exception ex)
		{
			PublishPollingFailure(ex);
			return;
		}

		EventHandler<BatteryInfoChangedEventArgs>? batteryHandler = null;
		EventHandler<EnergySaverStatusChangedEventArgs>? energyHandler = null;
		PowerSnapshot previous;

		lock (_gate)
		{
			previous = _snapshot;
			_snapshot = current;

			if (previous.ChargeLevel != current.ChargeLevel ||
				previous.State != current.State ||
				previous.PowerSource != current.PowerSource)
			{
				batteryHandler = _batteryInfoChanged;
			}

			if (previous.EnergySaverStatus != current.EnergySaverStatus)
				energyHandler = _energySaverStatusChanged;
		}

		try
		{
			batteryHandler?.Invoke(this,
				new BatteryInfoChangedEventArgs(current.ChargeLevel, current.State, current.PowerSource));
			energyHandler?.Invoke(this, new EnergySaverStatusChangedEventArgs(current.EnergySaverStatus));
		}
		catch (Exception ex)
		{
			PublishPollingFailure(ex);
		}
	}

	void PublishPollingFailure(Exception error)
	{
		Console.Error.WriteLine($"[WPFBattery] Unable to refresh power status: {error.Message}");
		try
		{
			PollingFailed?.Invoke(this, error);
		}
		catch (Exception callbackError)
		{
			Console.Error.WriteLine(
				$"[WPFBattery] Polling failure callback failed: {callbackError.Message}");
		}
	}

	internal static PowerSnapshot ReadSnapshot()
	{
		if (!GetSystemPowerStatus(out var status))
			throw new InvalidOperationException(
				$"GetSystemPowerStatus failed with Win32 error {Marshal.GetLastWin32Error()}.");

		return MapSnapshot(status);
	}

	internal static PowerSnapshot MapSnapshot(SystemPowerStatus status)
	{
		var batteryStatusUnknown = status.BatteryFlag == byte.MaxValue;
		var batteryPresent = !batteryStatusUnknown && (status.BatteryFlag & 128) == 0;
		var chargeLevel = !batteryPresent || status.BatteryLifePercent == byte.MaxValue
			? -1.0
			: Math.Clamp(status.BatteryLifePercent, (byte)0, (byte)100) / 100.0;
		var powerSource = status.ACLineStatus switch
		{
			0 => BatteryPowerSource.Battery,
			1 => BatteryPowerSource.AC,
			_ => BatteryPowerSource.Unknown,
		};
		var batteryState = batteryStatusUnknown
			? BatteryState.Unknown
			: !batteryPresent
			? BatteryState.NotPresent
			: (status.BatteryFlag & 8) != 0
				? BatteryState.Charging
				: status.BatteryLifePercent == 100
					? BatteryState.Full
					: status.ACLineStatus == 0
						? BatteryState.Discharging
						: BatteryState.NotCharging;
		var energySaverStatus = (status.SystemStatusFlag & 1) != 0
			? EnergySaverStatus.On
			: EnergySaverStatus.Off;

		return new PowerSnapshot(chargeLevel, batteryState, powerSource, energySaverStatus);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_timer?.Dispose();
			_timer = null;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

	[StructLayout(LayoutKind.Sequential)]
	internal struct SystemPowerStatus
	{
		public byte ACLineStatus;
		public byte BatteryFlag;
		public byte BatteryLifePercent;
		public byte SystemStatusFlag;
		public uint BatteryLifeTime;
		public uint BatteryFullLifeTime;
	}

	internal readonly record struct PowerSnapshot(
		double ChargeLevel,
		BatteryState State,
		BatteryPowerSource PowerSource,
		EnergySaverStatus EnergySaverStatus);
}
