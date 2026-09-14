using System;
namespace Comet
{
	/// <summary>
	/// Press-and-hold voice-record gesture — the gold Jetchat <c>RecordButton</c>'s
	/// <c>detectDragGesturesAfterLongPress</c> (RecordButton.kt): a long-press starts
	/// recording, a horizontal drag tracks the swipe-to-cancel offset, releasing finishes,
	/// and dragging left past the threshold cancels. The backend drives the phases via
	/// <see cref="Begin"/> / <see cref="Drag"/> / <see cref="End"/> / <see cref="Cancel"/>;
	/// the accumulated offset (<see cref="TotalX"/> / <see cref="TotalY"/> in dp) and the
	/// <see cref="Status"/> are read by the handler each invoke.
	/// </summary>
	public class RecordGesture : Gesture<RecordGesture>
	{
		public RecordGesture(Action<RecordGesture> action) : base(action) { }

		/// <summary>Phase of the gesture: Started (hold engaged) → Running (dragging) →
		/// Completed (released) / Canceled (lifted early or swiped past the threshold).</summary>
		public GestureStatus Status { get; private set; }

		/// <summary>Cumulative horizontal drag in dp (negative = leftward = toward cancel).</summary>
		public double TotalX { get; private set; }

		/// <summary>Cumulative vertical drag in dp.</summary>
		public double TotalY { get; private set; }

		// Gold thresholds (RecordButton.kt voiceRecordingGesture): swipe left ≥200dp while
		// staying within ±80dp vertically cancels the in-progress recording.
		const double CancelThresholdDp = 200;
		const double VerticalThresholdDp = 80;

		bool _dragging;

		/// <summary>Long-press engaged — begin recording.</summary>
		public void Begin()
		{
			TotalX = TotalY = 0;
			_dragging = true;
			Status = GestureStatus.Started;
			Invoke();
		}

		/// <summary>A per-frame drag delta (dp) while held; accumulates the offset and
		/// auto-cancels once the leftward swipe passes the threshold.</summary>
		public void Drag(double dx, double dy)
		{
			if (!_dragging)
				return;
			TotalX += dx;
			TotalY += dy;
			Status = GestureStatus.Running;
			if (TotalX < 0 && Math.Abs(TotalX) >= CancelThresholdDp && Math.Abs(TotalY) <= VerticalThresholdDp)
			{
				_dragging = false;
				Status = GestureStatus.Canceled;
			}
			Invoke();
		}

		/// <summary>Pointer released — finish recording (no-op if already cancelled).</summary>
		public void End()
		{
			if (!_dragging)
				return;
			_dragging = false;
			Status = GestureStatus.Completed;
			Invoke();
		}

		/// <summary>Gesture cancelled by the system (parent intercept, etc.).</summary>
		public void Cancel()
		{
			_dragging = false;
			Status = GestureStatus.Canceled;
			Invoke();
		}
	}
}
