using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Gesture recognizer support for GTK4.
/// Maps MAUI gesture recognizers to GTK4 gesture controllers through the public MAUI 11
/// gesture APIs (<c>SendTapped</c>, <c>SendPointer*</c>, <c>SendSwiped</c> and the
/// <see cref="IPanGestureController"/>/<see cref="IPinchGestureController"/> interfaces).
/// </summary>
public static class GtkGestureExtensions
{
	const uint SecondaryMouseButton = 3;

	// Track attached controllers per widget to enable cleanup
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
					controllers.AddRange(AttachTapGesture(widget, tapRecognizer));
					break;
				case PanGestureRecognizer panRecognizer:
					controllers.AddRange(AttachPanGesture(widget, panRecognizer));
					break;
				case PointerGestureRecognizer pointerRecognizer:
					controllers.AddRange(AttachPointerGesture(widget, pointerRecognizer));
					break;
				case SwipeGestureRecognizer swipeRecognizer:
					controllers.AddRange(AttachSwipeGesture(widget, swipeRecognizer));
					break;
				case PinchGestureRecognizer pinchRecognizer:
					controllers.AddRange(AttachPinchGesture(widget, pinchRecognizer));
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
			foreach (var c in controllers)
				widget.RemoveController(c);
		}
	}

	private static Gtk.EventController[] AttachTapGesture(Gtk.Widget widget, TapGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureClick.New();
		gesture.SetButton(0); // all buttons; filtered against TapGestureRecognizer.Buttons
		gesture.OnReleased += (_, args) =>
		{
			// SendTapped executes Command and raises Tapped; it requires the owning view.
			var sender = PanGestureTracker.FindParentView(recognizer);
			var button = ToButtonsMask(gesture.GetCurrentButton());
			if (sender is not null && (recognizer.Buttons & button) != 0 && args.NPress >= recognizer.NumberOfTapsRequired)
				recognizer.SendTapped(sender, CreateGetPosition(widget, args.X, args.Y));
		};
		widget.AddController(gesture);
		return [gesture];
	}

	private static Gtk.EventController[] AttachPanGesture(Gtk.Widget widget, PanGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureDrag.New();
		var tracker = new PanGestureTracker(recognizer);

		gesture.OnDragBegin += (_, _) => tracker.Begin();
		gesture.OnDragUpdate += (_, args) => tracker.Update(args.OffsetX, args.OffsetY);
		// GTK emits cancel before drag-end; the tracker ignores the end of a canceled pan.
		gesture.OnCancel += (_, _) => tracker.Cancel();
		gesture.OnDragEnd += (_, _) => tracker.End();

		widget.AddController(gesture);
		return [gesture];
	}

	private static Gtk.EventController[] AttachPointerGesture(Gtk.Widget widget, PointerGestureRecognizer recognizer)
	{
		var motion = Gtk.EventControllerMotion.New();
		double lastX = 0, lastY = 0;

		// The PointerGestureRecognizer Send* methods execute the commands and raise the events;
		// they require the owning view.
		motion.OnEnter += (_, args) =>
		{
			lastX = args.X;
			lastY = args.Y;
			var sender = PanGestureTracker.FindParentView(recognizer);
			if (sender is not null)
				recognizer.SendPointerEntered(sender, CreateGetPosition(widget, lastX, lastY));
		};
		motion.OnMotion += (_, args) =>
		{
			lastX = args.X;
			lastY = args.Y;
			var sender = PanGestureTracker.FindParentView(recognizer);
			if (sender is not null)
				recognizer.SendPointerMoved(sender, CreateGetPosition(widget, lastX, lastY));
		};
		motion.OnLeave += (_, _) =>
		{
			var sender = PanGestureTracker.FindParentView(recognizer);
			if (sender is not null)
				recognizer.SendPointerExited(sender, CreateGetPosition(widget, lastX, lastY));
		};

		// Pointer press/release
		var click = Gtk.GestureClick.New();
		click.SetButton(0); // all buttons
		click.OnPressed += (_, args) =>
		{
			var sender = PanGestureTracker.FindParentView(recognizer);
			if (sender is not null)
				recognizer.SendPointerPressed(sender, CreateGetPosition(widget, args.X, args.Y), button: ToButtonsMask(click.GetCurrentButton()));
		};
		click.OnReleased += (_, args) =>
		{
			var sender = PanGestureTracker.FindParentView(recognizer);
			if (sender is not null)
				recognizer.SendPointerReleased(sender, CreateGetPosition(widget, args.X, args.Y), button: ToButtonsMask(click.GetCurrentButton()));
		};

		widget.AddController(motion);
		widget.AddController(click);
		return [motion, click];
	}

	private static Gtk.EventController[] AttachSwipeGesture(Gtk.Widget widget, SwipeGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureSwipe.New();
		gesture.OnSwipe += (_, args) =>
		{
			var mauiView = recognizer.Parent as View;
			if (mauiView == null) return;

			double vx = args.VelocityX;
			double vy = args.VelocityY;
			SwipeDirection direction;

			if (Math.Abs(vx) > Math.Abs(vy))
				direction = vx > 0 ? SwipeDirection.Right : SwipeDirection.Left;
			else
				direction = vy > 0 ? SwipeDirection.Down : SwipeDirection.Up;

			if (recognizer.Direction.HasFlag(direction))
				recognizer.SendSwiped(mauiView, direction);
		};
		widget.AddController(gesture);
		return [gesture];
	}

	private static Gtk.EventController[] AttachPinchGesture(Gtk.Widget widget, PinchGestureRecognizer recognizer)
	{
		var gesture = Gtk.GestureZoom.New();
		var controller = (IPinchGestureController)recognizer;
		double startScale = 1.0;
		var sender = recognizer.Parent as Element ?? recognizer;

		gesture.OnBegin += (_, _) =>
		{
			startScale = gesture.GetScaleDelta();
			if (startScale <= 0) startScale = 1.0;
			controller.SendPinchStarted(sender, new Point(0, 0));
		};
		gesture.OnScaleChanged += (_, args) =>
		{
			controller.SendPinch(sender, args.Scale / startScale, new Point(0, 0));
		};
		gesture.OnEnd += (_, _) =>
		{
			controller.SendPinchEnded(sender);
		};
		gesture.OnCancel += (_, _) =>
		{
			controller.SendPinchCanceled(sender);
		};

		widget.AddController(gesture);
		return [gesture];
	}

	/// <summary>Maps a GDK button number to the MAUI button mask (button 3 is the secondary button).</summary>
	internal static ButtonsMask ToButtonsMask(uint button) =>
		button == SecondaryMouseButton ? ButtonsMask.Secondary : ButtonsMask.Primary;

	static Func<IElement?, Point?> CreateGetPosition(Gtk.Widget widget, double x, double y) =>
		relativeTo => ResolvePosition(widget, x, y, relativeTo);

	/// <summary>
	/// Converts a position in <paramref name="widget"/>'s coordinates for MAUI's <c>GetPosition(relativeTo)</c>.
	/// A <see langword="null"/> <paramref name="relativeTo"/> yields window coordinates, as MAUI does on
	/// Windows and iOS; an element without a GTK platform view, or in an unrelated widget tree, yields
	/// <see langword="null"/>.
	/// </summary>
	static Point? ResolvePosition(Gtk.Widget widget, double x, double y, IElement? relativeTo)
	{
		var target = relativeTo is null
			? widget.GetRoot() as Gtk.Widget
			: relativeTo.Handler?.PlatformView as Gtk.Widget;
		if (target is null)
			return null;
		if (target.Handle.DangerousGetHandle() == widget.Handle.DangerousGetHandle())
			return new Point(x, y);
		return widget.TranslateCoordinates(target, x, y, out var destX, out var destY)
			? new Point(destX, destY)
			: null;
	}
}
