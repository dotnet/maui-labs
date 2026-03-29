using Microsoft.Maui;
using Microsoft.Maui.Dispatching;

namespace Platform.Maui.Linux.Gtk4.Platform;

public class GtkDispatcherProvider : IDispatcherProvider
{
	public IDispatcher? GetForCurrentThread()
	{
		return new GtkDispatcher();
	}
}

public class GtkDispatcher : IDispatcher
{
	public bool IsDispatchRequired => !GLib.Functions.MainContextDefault().IsOwner();

	public IDispatcherTimer CreateTimer()
	{
		return new GtkDispatcherTimer();
	}

	public bool Dispatch(Action action)
	{
		if (IsDispatchRequired)
		{
			GLib.Functions.IdleAdd(0, () =>
			{
				action();
				return false; // run once
			});
		}
		else
		{
			action();
		}
		return true;
	}

	public bool DispatchDelayed(TimeSpan delay, Action action)
	{
		GLib.Functions.TimeoutAdd(0, (uint)delay.TotalMilliseconds, () =>
		{
			action();
			return false; // run once
		});
		return true;
	}
}

public class GtkDispatcherTimer : IDispatcherTimer
{
	private uint _sourceId;
	private bool _isRunning;

	public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(16);
	public bool IsRepeating { get; set; } = true;
	public bool IsRunning => _isRunning;

	public event EventHandler? Tick;

	public void Start()
	{
		if (_isRunning)
			return;

		_isRunning = true;
		_sourceId = GLib.Functions.TimeoutAdd(0, (uint)Interval.TotalMilliseconds, () =>
		{
			Tick?.Invoke(this, EventArgs.Empty);
			return _isRunning && IsRepeating;
		});
	}

	public void Stop()
	{
		if (!_isRunning)
			return;

		_isRunning = false;
		if (_sourceId > 0)
		{
			GLib.Internal.Source.Remove(_sourceId);
			_sourceId = 0;
		}
	}
}
