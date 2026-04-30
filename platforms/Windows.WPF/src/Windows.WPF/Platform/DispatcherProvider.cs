using System;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public partial class WPFDispatcherProvider : IDispatcherProvider
	{
		[ThreadStatic]
		static IDispatcher? s_dispatcherInstance;

		/// <inheritdoc/>
		public IDispatcher? GetForCurrentThread()
		{
			// Only return a dispatcher for threads that already have one pumping messages.
			// Dispatcher.CurrentDispatcher creates a new one for any thread — avoid that.
			var dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
			if (dispatcher == null)
				return null;

			return s_dispatcherInstance ??= new WPFDispatcher(dispatcher);
		}
	}

	public partial class WPFDispatcher : IDispatcher
	{
		internal static System.Windows.Threading.Dispatcher? DispatcherOverride { get; set; }

		readonly System.Windows.Threading.Dispatcher _dispatcherQueue;
		public System.Windows.Threading.Dispatcher Dispatcher => DispatcherOverride ?? _dispatcherQueue;

		internal WPFDispatcher(System.Windows.Threading.Dispatcher dispatcherQueue)
		{
			_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		}

		public bool IsDispatchRequired
		{
			get
			{
				var result = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
				return result != Dispatcher;
			}
		}

		public bool Dispatch(Action action)
		{
			Dispatcher.BeginInvoke(action, null);
			return true;
		}

		public bool DispatchDelayed(TimeSpan delay, Action action)
		{
			var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
			{
				Interval = delay
			};
			timer.Tick += (s, e) =>
			{
				timer.Stop();
				action();
			};
			timer.Start();
			return true;
		}

		public IDispatcherTimer CreateTimer()
		{
			return new WPFDispatchTimer(this);
		}
	}

	public class WPFDispatchTimer : IDispatcherTimer
	{
		readonly DispatcherTimer _dispatchTimer;
		bool _isRunning;

		public WPFDispatchTimer(WPFDispatcher wPFDispatcher)
		{
			_dispatchTimer = new DispatcherTimer(DispatcherPriority.Normal, wPFDispatcher.Dispatcher);
			_dispatchTimer.Tick += OnTick;
		}

		void OnTick(object? sender, EventArgs e)
		{
			Tick?.Invoke(this, EventArgs.Empty);

			if (!IsRepeating)
				Stop();
		}

		public TimeSpan Interval
		{
			get => _dispatchTimer.Interval;
			set => _dispatchTimer.Interval = value;
		}

		public bool IsRepeating { get; set; }

		public bool IsRunning => _isRunning;

		public event EventHandler? Tick;

		public void Start()
		{
			_isRunning = true;
			_dispatchTimer.Start();
		}

		public void Stop()
		{
			_isRunning = false;
			_dispatchTimer.Stop();
		}
	}
}
