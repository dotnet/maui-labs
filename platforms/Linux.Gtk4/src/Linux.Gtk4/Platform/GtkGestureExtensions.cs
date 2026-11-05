using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Gesture recognizer support for GTK4.
/// Maps MAUI gesture recognizers to GTK4 gesture controllers.
/// </summary>
public static class GtkGestureExtensions
{
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, List<Gtk.EventController>> _attachedControllers = new();

/// <summary>
/// Attaches MAUI gesture recognizers to a GTK widget.
/// Removes any previously attached gesture controllers first to prevent accumulation.
/// </summary>
	public static void AttachGestures(Gtk.Widget widget, IView view)
	{
		if (view is not View mauiView)
			return;

		RemoveAttachedGestures(widget);

		var controllers = new List<Gtk.EventController>();

		foreach (var recognizer in mauiView.GestureRecognizers)
		{
			switch (recognizer)
			{
				case TapGestureRecognizer tapRecognizer:
					controllers.AddRange(AttachTapGesture(widget, mauiView, tapRecognizer));
					break;
				case PanGestureRecognizer panRecognizer:
					controllers.AddRange(AttachPanGesture(widget, mauiView, panRecognizer));
					break;
				case PointerGestureRecognizer pointerRecognizer:
					controllers.AddRange(AttachPointerGesture(widget, pointerRecognizer));
					break;
				case SwipeGestureRecognizer swipeRecognizer:
					controllers.AddRange(AttachSwipeGesture(widget, mauiView, swipeRecognizer));
					break;
				case PinchGestureRecognizer pinchRecognizer:
					controllers.AddRange(AttachPinchGesture(widget, mauiView, pinchRecognizer));
					break;
			}
		}

		if (controllers.Count > 0)
			_attachedControllers[widget.Handle.DangerousGetHandle()] = controllers;
	}

/// <summary>
/// Removes all gesture controllers previously attached by <see cref="AttachGestures"/>.
/// </summary>
	public static void RemoveAttachedGestures(Gtk.Widget widget)
	{
		if (_attachedControllers.TryRemove(widget.Handle.DangerousGetHandle(), out var controllers))
		{
			foreach (var controller in controllers)
				widget.RemoveController(controller);
		}
	}

	private static Gtk.EventController[] AttachTapGesture(Gtk.Widget widget, View view, TapGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureClick.New();
#if NET11_0_OR_GREATER
		gesture.OnReleased += (_, args) =>
			recognizer.SendTapped(view, _ => new Point(args.X, args.Y));
#else
		gesture.OnReleased += (_, _) =>
			recognizer.Command?.Execute(recognizer.CommandParameter);
#endif
		widget.AddController(gesture);
		return [gesture];
	}

	private static Gtk.EventController[] AttachPanGesture(Gtk.Widget widget, View view, PanGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureDrag.New();
#if NET11_0_OR_GREATER
		var controller = (IPanGestureController)recognizer;
#endif
		var gestureId = 0;

		gesture.OnDragBegin += (_, _) =>
		{
			gestureId++;
#if NET11_0_OR_GREATER
			controller.SendPanStarted(view, gestureId);
#else
			SendPanNet10(recognizer, GestureStatus.Started, gestureId, 0, 0);
#endif
		};
#if NET11_0_OR_GREATER
		gesture.OnDragUpdate += (_, args) =>
			controller.SendPan(view, args.OffsetX, args.OffsetY, gestureId);
		gesture.OnDragEnd += (_, _) =>
			controller.SendPanCompleted(view, gestureId);
		gesture.OnCancel += (_, _) =>
			controller.SendPanCanceled(view, gestureId);
#else
		gesture.OnDragUpdate += (_, args) =>
			SendPanNet10(recognizer, GestureStatus.Running, gestureId, args.OffsetX, args.OffsetY);
		gesture.OnDragEnd += (_, _) =>
			SendPanNet10(recognizer, GestureStatus.Completed, gestureId, 0, 0);
		gesture.OnCancel += (_, _) =>
			SendPanNet10(recognizer, GestureStatus.Canceled, gestureId, 0, 0);
#endif
		widget.AddController(gesture);
		return [gesture];
	}

#if NET10_0
	private static void SendPanNet10(
		PanGestureRecognizer recognizer,
		GestureStatus status,
		int gestureId,
		double totalX,
		double totalY)
	{
		var method = typeof(PanGestureRecognizer).GetMethod(
			"SendPan",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		if (method is not null)
		{
			try
			{
				method.Invoke(recognizer, [status, gestureId, totalX, totalY]);
			}
			catch
			{
				// Preserve the MAUI 10 best-effort compatibility behavior.
			}
			return;
		}

		var legacyMethod = typeof(PanGestureRecognizer).GetMethod(
			"SendPanUpdated",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		try
		{
			legacyMethod?.Invoke(recognizer, [new PanUpdatedEventArgs(status, gestureId, totalX, totalY)]);
		}
		catch
		{
			// Preserve the MAUI 10 best-effort compatibility behavior.
		}
	}
#endif

	private static Gtk.EventController[] AttachPointerGesture(Gtk.Widget widget, PointerGestureRecognizer recognizer)
	{
		var motion = Gtk.EventControllerMotion.New();

		motion.OnEnter += (_, _) =>
			Execute(recognizer.PointerEnteredCommand, recognizer.PointerEnteredCommandParameter);
		motion.OnMotion += (_, _) =>
			Execute(recognizer.PointerMovedCommand, recognizer.PointerMovedCommandParameter);
		motion.OnLeave += (_, _) =>
			Execute(recognizer.PointerExitedCommand, recognizer.PointerExitedCommandParameter);

		var click = Gtk.GestureClick.New();
		click.OnPressed += (_, _) =>
			Execute(recognizer.PointerPressedCommand, recognizer.PointerPressedCommandParameter);
		click.OnReleased += (_, _) =>
			Execute(recognizer.PointerReleasedCommand, recognizer.PointerReleasedCommandParameter);

		widget.AddController(motion);
		widget.AddController(click);
		return [motion, click];
	}

	private static Gtk.EventController[] AttachSwipeGesture(Gtk.Widget widget, View view, SwipeGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureSwipe.New();
		gesture.OnSwipe += (_, args) =>
		{
			var velocityX = args.VelocityX;
			var velocityY = args.VelocityY;
			var direction = Math.Abs(velocityX) > Math.Abs(velocityY)
				? velocityX > 0 ? SwipeDirection.Right : SwipeDirection.Left
				: velocityY > 0 ? SwipeDirection.Down : SwipeDirection.Up;

			if (recognizer.Direction.HasFlag(direction))
				recognizer.SendSwiped(view, direction);
		};
		widget.AddController(gesture);
		return [gesture];
	}

	private static Gtk.EventController[] AttachPinchGesture(Gtk.Widget widget, View view, PinchGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureZoom.New();
		var controller = (IPinchGestureController)recognizer;
		var startScale = 1.0;

		gesture.OnBegin += (_, _) =>
		{
			startScale = gesture.GetScaleDelta();
			if (startScale <= 0)
				startScale = 1.0;
			controller.SendPinchStarted(view, new Point(0, 0));
		};
		gesture.OnScaleChanged += (_, args) =>
			controller.SendPinch(view, args.Scale / startScale, new Point(0, 0));
		gesture.OnEnd += (_, _) =>
			controller.SendPinchEnded(view);
		gesture.OnCancel += (_, _) =>
			controller.SendPinchCanceled(view);

		widget.AddController(gesture);
		return [gesture];
	}

	static void Execute(System.Windows.Input.ICommand? command, object? parameter)
	{
		if (command?.CanExecute(parameter) == true)
			command.Execute(parameter);
	}
}
