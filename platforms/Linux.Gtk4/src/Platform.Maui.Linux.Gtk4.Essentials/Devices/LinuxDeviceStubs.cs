using Microsoft.Maui.Devices;

namespace Platform.Maui.Linux.Gtk4.Essentials.Devices;

public class LinuxFlashlight : IFlashlight
{
	public Task<bool> IsSupportedAsync() => Task.FromResult(false);
	public Task TurnOnAsync() => throw new PlatformNotSupportedException("Flashlight is not available on Linux desktop.");
	public Task TurnOffAsync() => throw new PlatformNotSupportedException("Flashlight is not available on Linux desktop.");
}

public class LinuxHapticFeedback : IHapticFeedback
{
	public bool IsSupported => false;
	public void Perform(HapticFeedbackType type) { }
}

public class LinuxVibration : IVibration
{
	public bool IsSupported => false;
	public void Vibrate() { }
	public void Vibrate(TimeSpan duration) { }
	public void Cancel() { }
}
