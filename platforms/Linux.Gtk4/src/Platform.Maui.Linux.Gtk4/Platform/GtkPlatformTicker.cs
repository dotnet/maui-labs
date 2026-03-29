using Microsoft.Maui.Animations;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// GTK4 implementation of <see cref="ITicker"/> that drives MAUI animations
/// using GLib's main loop timeout at ~60fps.
/// </summary>
public class GtkPlatformTicker : Ticker
{
	uint _timerId;

	public override bool IsRunning => _timerId != 0;

	public override void Start()
	{
		if (_timerId != 0)
			return;

		int intervalMs = MaxFps > 0 ? 1000 / MaxFps : 16; // ~60fps default

		_timerId = GLib.Functions.TimeoutAdd(0, (uint)intervalMs, () =>
		{
			Fire?.Invoke();
			return _timerId != 0; // keep running while active
		});
	}

	public override void Stop()
	{
		if (_timerId != 0)
		{
			GLib.Functions.SourceRemove(_timerId);
			_timerId = 0;
		}
	}
}
