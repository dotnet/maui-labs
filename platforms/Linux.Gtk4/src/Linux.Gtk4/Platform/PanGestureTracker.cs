using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Translates a platform drag (begin, offset updates, end or cancel) into the
/// <see cref="IPanGestureController"/> calls that raise <see cref="PanGestureRecognizer.PanUpdated"/>.
/// </summary>
/// <remarks>
/// A pan starts on the first movement rather than on press, so a plain click does not report
/// Started/Completed. Each pan gets a new gesture id, shared by all of its updates.
/// </remarks>
internal sealed class PanGestureTracker
{
	static int s_nextGestureId;

	readonly PanGestureRecognizer _recognizer;
	bool _isPressed;
	bool _isPanning;
	int _gestureId;

	public PanGestureTracker(PanGestureRecognizer recognizer)
	{
		_recognizer = recognizer;
	}

	public bool IsPanning => _isPanning;

	public void Begin()
	{
		_isPressed = true;
		_isPanning = false;
	}

	/// <summary>Reports the total offset from the point where the drag began.</summary>
	public void Update(double totalX, double totalY)
	{
		var sender = FindParentView(_recognizer);
		if (!_isPressed || sender is null)
			return;

		IPanGestureController controller = _recognizer;
		if (!_isPanning)
		{
			_isPanning = true;
			_gestureId = Interlocked.Increment(ref s_nextGestureId);
			controller.SendPanStarted(sender, _gestureId);
		}

		controller.SendPan(sender, totalX, totalY, _gestureId);
	}

	public void End()
	{
		var wasPanning = _isPanning;
		Reset();
		var sender = FindParentView(_recognizer);
		if (wasPanning && sender is not null)
			((IPanGestureController)_recognizer).SendPanCompleted(sender, _gestureId);
	}

	public void Cancel()
	{
		var wasPanning = _isPanning;
		Reset();
		var sender = FindParentView(_recognizer);
		if (wasPanning && sender is not null)
			((IPanGestureController)_recognizer).SendPanCanceled(sender, _gestureId);
	}

	void Reset()
	{
		_isPressed = false;
		_isPanning = false;
	}

	internal static View? FindParentView(IGestureRecognizer recognizer)
	{
		var current = (recognizer as Element)?.Parent;
		while (current is not null && current is not View)
			current = current.Parent;
		return current as View;
	}
}
