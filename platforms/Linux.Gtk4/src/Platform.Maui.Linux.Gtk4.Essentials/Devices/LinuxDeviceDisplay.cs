using Microsoft.Maui.Devices;

namespace Platform.Maui.Linux.Gtk4.Essentials.Devices;

public class LinuxDeviceDisplay : IDeviceDisplay
{
	private bool _keepScreenOn;
	private EventHandler<DisplayInfoChangedEventArgs>? _mainDisplayInfoChanged;

	public bool KeepScreenOn
	{
		get => _keepScreenOn;
		set => _keepScreenOn = value; // Best-effort: no standard inhibit API short of DBus org.freedesktop.ScreenSaver
	}

	public DisplayInfo MainDisplayInfo
	{
		get
		{
			try
			{
				var display = Gdk.Display.GetDefault();
				if (display is null)
					return new DisplayInfo(1920, 1080, 1.0, DisplayOrientation.Landscape, DisplayRotation.Rotation0);

				var monitors = display.GetMonitors();
				if (monitors.GetNItems() == 0)
					return new DisplayInfo(1920, 1080, 1.0, DisplayOrientation.Landscape, DisplayRotation.Rotation0);

				var monitor = (Gdk.Monitor)monitors.GetObject(0)!;
				monitor.GetGeometry(out var geometry);
				var scale = monitor.GetScaleFactor();
				var width = geometry.Width * scale;
				var height = geometry.Height * scale;
				var orientation = width >= height ? DisplayOrientation.Landscape : DisplayOrientation.Portrait;
				var refreshRate = monitor.GetRefreshRate() / 1000.0f; // GDK returns mHz

				return new DisplayInfo(width, height, scale, orientation, DisplayRotation.Rotation0, refreshRate);
			}
			catch
			{
				return new DisplayInfo(1920, 1080, 1.0, DisplayOrientation.Landscape, DisplayRotation.Rotation0);
			}
		}
	}

	public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged
	{
		add => _mainDisplayInfoChanged += value;
		remove => _mainDisplayInfoChanged -= value;
	}
}
