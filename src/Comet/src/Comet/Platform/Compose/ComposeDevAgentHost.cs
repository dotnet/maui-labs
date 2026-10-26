#nullable enable
#if ANDROID
using System;
using System.Diagnostics;
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

			bool Dispatch(
				MotionEventActions action,
				float x,
				float y,
				int timeoutMs,
				bool cancelIfQueued,
				bool waitIfStarted)
			{
				var completion = new TaskCompletionSource<bool>(
					TaskCreationOptions.RunContinuationsAsynchronously);
				var dispatchState = 0; // 0 = queued, 1 = running, 2 = canceled
				activity.RunOnUiThread(() =>
				{
					if (Interlocked.CompareExchange(ref dispatchState, 1, 0) != 0)
					{
						completion.TrySetResult(false);
						return;
					}

					var dispatched = true;
					try
					{
						long now = SystemClock.UptimeMillis();
						if (action == MotionEventActions.Down)
							downTime = now;
						var ev = MotionEvent.Obtain(downTime, now, action, x, y, 0);
						if (ev is null) { dispatched = false; return; }
						try
						{
							ev.SetSource(InputSourceType.Touchscreen);
							if (!decor.DispatchTouchEvent(ev))
								dispatched = action != MotionEventActions.Down; // nobody claimed the down → nothing will track the drag
						}
						finally { ev.Recycle(); }
					}
					catch { dispatched = false; }
					finally { completion.TrySetResult(dispatched); }
				});

				if (completion.Task.Wait(timeoutMs))
					return completion.Task.Result;

				if (!cancelIfQueued)
					return false;

				if (Interlocked.CompareExchange(ref dispatchState, 2, 0) == 0)
					return false;

				if (!waitIfStarted)
					return false;

				// A started Down must finish so the caller knows whether it needs a terminal event.
				completion.Task.Wait();
				return completion.Task.Result;
			}

			if (!Dispatch(
				MotionEventActions.Down,
				x1,
				y1,
				timeoutMs: 2000,
				cancelIfQueued: true,
				waitIfStarted: true))
				return false;

			var timer = Stopwatch.StartNew();
			var deadlineMs = durationMs + 2000L;
			var ok = true;
			var lastX = x1;
			var lastY = y1;
			for (int i = 1; i <= steps; i++)
			{
				var targetElapsedMs = durationMs * i / steps;
				var delayMs = targetElapsedMs - timer.ElapsedMilliseconds;
				if (delayMs > 0)
					Thread.Sleep((int)delayMs);
				if (timer.ElapsedMilliseconds > deadlineMs)
				{
					ok = false;
					break;
				}

				float t = i / (float)steps;
				var x = x1 + (x2 - x1) * t;
				var y = y1 + (y2 - y1) * t;
				var remainingBudgetMs = deadlineMs - timer.ElapsedMilliseconds;
				if (remainingBudgetMs <= 0 ||
					!Dispatch(
						MotionEventActions.Move,
						x,
						y,
						timeoutMs: (int)Math.Min(2000L, remainingBudgetMs),
						cancelIfQueued: true,
						waitIfStarted: false) ||
					timer.ElapsedMilliseconds > deadlineMs)
				{
					ok = false;
					break;
				}
				lastX = x;
				lastY = y;
			}

			// Once Down is delivered, the terminal event cannot be canceled while queued.
			// It must close the native gesture when the UI thread becomes responsive.
			var terminalAction = ok ? MotionEventActions.Up : MotionEventActions.Cancel;
			var terminalX = ok ? x2 : lastX;
			var terminalY = ok ? y2 : lastY;
			var terminalBudgetMs = (int)Math.Max(0L, deadlineMs - timer.ElapsedMilliseconds);
			var terminated = Dispatch(
				terminalAction,
				terminalX,
				terminalY,
				timeoutMs: terminalBudgetMs,
				cancelIfQueued: false,
				waitIfStarted: false);
			return ok && terminated;
		}
	}
}
#endif
