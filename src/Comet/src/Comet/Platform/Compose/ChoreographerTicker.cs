#nullable enable
#if ANDROID
using System;
using System.Collections.Generic;
using Android.Views;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// <see cref="Backend.ICometFrameTicker"/> over Android's <see cref="Choreographer"/> —
	/// the vsync-aligned frame pump that drives Comet's animation engine on the node
	/// backend. Keeps a single pending frame callback while anyone is subscribed.
	/// </summary>
	/// <remarks>Choreographer is thread-affine: subscriptions must come from the main
	/// thread (Comet's animation plumbing already marshals adds via ThreadHelper).</remarks>
	sealed class ChoreographerTicker : Java.Lang.Object, Backend.ICometFrameTicker, Choreographer.IFrameCallback
	{
		readonly List<Action<TimeSpan>> _subscribers = new();
		bool _posted;
		long _lastFrameNanos;

		public IDisposable Subscribe(Action<TimeSpan> onFrame)
		{
			lock (_subscribers)
			{
				_subscribers.Add(onFrame);
				if (!_posted)
				{
					_posted = true;
					_lastFrameNanos = 0;
					Choreographer.Instance!.PostFrameCallback(this);
				}
			}
			return new Subscription(this, onFrame);
		}

		public void DoFrame(long frameTimeNanos)
		{
			Action<TimeSpan>[] subscribers;
			lock (_subscribers)
			{
				if (_subscribers.Count == 0)
				{
					_posted = false;
					return;
				}
				subscribers = _subscribers.ToArray();
				Choreographer.Instance!.PostFrameCallback(this);
			}

			// 100ns ticks per nano ÷ 100; first frame after an idle gap reports zero elapsed.
			var elapsed = _lastFrameNanos == 0
				? TimeSpan.Zero
				: TimeSpan.FromTicks((frameTimeNanos - _lastFrameNanos) / 100);
			_lastFrameNanos = frameTimeNanos;

			foreach (var subscriber in subscribers)
				subscriber(elapsed);
		}

		sealed class Subscription : IDisposable
		{
			ChoreographerTicker? _owner;
			readonly Action<TimeSpan> _callback;

			public Subscription(ChoreographerTicker owner, Action<TimeSpan> callback)
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
