#nullable enable
#if IOS
using System;
using System.Collections.Generic;
using CoreAnimation;
using Foundation;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// <see cref="Backend.ICometFrameTicker"/> over <see cref="CADisplayLink"/> — the
	/// vsync frame pump that drives Comet's animation engine on the SwiftUI backend
	/// (the iOS twin of the Compose ChoreographerTicker). The link is paused while no
	/// one is subscribed.
	/// </summary>
	sealed class DisplayLinkTicker : Backend.ICometFrameTicker
	{
		readonly List<Action<TimeSpan>> _subscribers = new();
		CADisplayLink? _link;
		double _lastTimestamp;

		public IDisposable Subscribe(Action<TimeSpan> onFrame)
		{
			lock (_subscribers)
			{
				_subscribers.Add(onFrame);
				if (_link is null)
				{
					_lastTimestamp = 0;
					_link = CADisplayLink.Create(OnFrame);
					_link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
				}
			}
			return new Subscription(this, onFrame);
		}

		void OnFrame()
		{
			Action<TimeSpan>[] subscribers;
			double timestamp = _link?.Timestamp ?? 0;
			lock (_subscribers)
			{
				if (_subscribers.Count == 0)
				{
					_link?.Invalidate();
					_link = null;
					return;
				}
				subscribers = _subscribers.ToArray();
			}

			var elapsed = _lastTimestamp == 0
				? TimeSpan.Zero
				: TimeSpan.FromSeconds(timestamp - _lastTimestamp);
			_lastTimestamp = timestamp;

			foreach (var subscriber in subscribers)
				subscriber(elapsed);
		}

		sealed class Subscription : IDisposable
		{
			DisplayLinkTicker? _owner;
			readonly Action<TimeSpan> _callback;

			public Subscription(DisplayLinkTicker owner, Action<TimeSpan> callback)
			{
				_owner = owner;
				_callback = callback;
			}

			public void Dispose()
			{
				var owner = _owner;
				_owner = null;
				if (owner is null)
					return;
				lock (owner._subscribers)
					owner._subscribers.Remove(_callback);
			}
		}
	}
}
#endif
