#nullable enable
#if ANDROID
using System;
using System.Threading;
using Android.OS;
using Android.Views;
using Comet.DevTools;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Hosts the <see cref="CometDevAgent"/> on Android so the same DevFlow-compatible
	/// surface that drives the SwiftUI probe (tree/elements + semantic tap/fill/clear/
	/// focus/back) drives Compose apps too — plus REAL coordinate drags: synthesized
	/// <see cref="MotionEvent"/> streams dispatched to the decor view, exercising the
	/// actual Compose gesture pipeline (pull-to-refresh, pager swipes, swipe-to-dismiss,
	/// flings — velocity falls out of the event timing, exactly as for a finger).
	/// </summary>
	/// <remarks>
	/// The agent binds localhost, so external tooling reaches it via
	/// <c>adb forward tcp:9223 tcp:9223</c> (a FIXED port a smoke script can rely on,
	/// unlike the broker-assigned MAUI-agent port that changes each launch). Screenshots
	/// are not served on Android — use <c>adb screencap</c> (the deployed smoke-script
	/// convention) or the probe's MAUI-agent PixelCopy route.
	/// </remarks>
	public static class ComposeDevAgentHost
	{
		static CometDevAgent? _agent;

		/// <summary>The running agent (null before <see cref="Start"/>); its
		/// <see cref="CometDevAgent.Port"/> is the port that actually bound.</summary>
		public static CometDevAgent? Agent => _agent;

		/// <summary>
		/// Registers the drag injector and starts the agent. Call BEFORE materializing the
		/// root view — <see cref="CometDevAgent.Start"/> enables <see cref="CometDevRegistry"/>
		/// tracking, and views materialized while it is disabled never enter the tree.
		/// Safe to call again on activity re-creation (the injector rebinds to the new
		/// activity; the agent itself starts once).
		/// </summary>
		public static void Start(global::Android.App.Activity activity, int port = CometDevAgent.DevFlowPort)
		{
			CometDevRegistry.DragInjector = (x1, y1, x2, y2, durationMs) =>
				InjectDrag(activity, x1, y1, x2, y2, durationMs);

			if (_agent is not null)
				return;

			var agent = new CometDevAgent(port, a => activity.RunOnUiThread(a));
			agent.Start();
			_agent = agent;
		}

		/// <summary>
		/// Synthesizes a touch drag as a Down → Move… → Up <see cref="MotionEvent"/> stream on
		/// the decor view, spacing the moves in real time so gesture-velocity trackers see an
		/// authentic finger. Blocks the calling (agent worker) thread for the gesture duration;
		/// each event is dispatched on the UI thread. Coordinates are physical pixels.
		/// </summary>
		internal static bool InjectDrag(global::Android.App.Activity activity,
			float x1, float y1, float x2, float y2, int durationMs)
		{
			var decor = activity.Window?.DecorView;
			if (decor is null)
				return false;
			if (Looper.MyLooper() == Looper.MainLooper)
				throw new InvalidOperationException("InjectDrag must not run on the UI thread (it blocks while dispatching to it)");

			// ~80Hz move stream: dense enough for Compose's velocity tracker even on
			// short flings, without flooding the main looper.
			int steps = Math.Max(2, durationMs / 12);
			long downTime = 0;
			bool ok = true;

			void Dispatch(MotionEventActions action, float x, float y)
			{
				using var done = new ManualResetEventSlim();
				activity.RunOnUiThread(() =>
				{
					try
					{
						long now = SystemClock.UptimeMillis();
						if (action == MotionEventActions.Down)
							downTime = now;
						var ev = MotionEvent.Obtain(downTime, now, action, x, y, 0);
						if (ev is null) { ok = false; return; }
						try
						{
							ev.SetSource(InputSourceType.Touchscreen);
							if (!decor.DispatchTouchEvent(ev))
								ok &= action != MotionEventActions.Down; // nobody claimed the down → nothing will track the drag
						}
						finally { ev.Recycle(); }
					}
					catch { ok = false; }
					finally { done.Set(); }
				});
				if (!done.Wait(2000))
					ok = false;
			}

			Dispatch(MotionEventActions.Down, x1, y1);
			if (!ok)
				return false;

			int interval = Math.Max(1, durationMs / steps);
			for (int i = 1; i <= steps && ok; i++)
			{
				Thread.Sleep(interval);
				float t = i / (float)steps;
				Dispatch(MotionEventActions.Move, x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
			}

			Dispatch(MotionEventActions.Up, x2, y2);
			return ok;
		}
	}
}
#endif
