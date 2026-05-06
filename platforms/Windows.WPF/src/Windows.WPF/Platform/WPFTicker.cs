#nullable enable
using System.Windows.Threading;
using Microsoft.Maui.Animations;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// WPF-specific animation ticker using DispatcherTimer on the UI thread.
	/// The default Ticker fires on threadpool threads which is unsafe for WPF —
	/// UI properties must be updated on the dispatcher thread.
	/// </summary>
	public class WPFTicker : Ticker
	{
		DispatcherTimer? _timer;

		public override bool IsRunning => _timer != null;

		public override void Start()
		{
			if (_timer != null)
				return;

			_timer = new DispatcherTimer(DispatcherPriority.Render)
			{
				Interval = System.TimeSpan.FromMilliseconds(1000.0 / MaxFps),
			};
			_timer.Tick += (s, e) => Fire?.Invoke();
			_timer.Start();
		}

		public override void Stop()
		{
			if (_timer == null)
				return;

			_timer.Stop();
			_timer = null;
		}
	}
}
