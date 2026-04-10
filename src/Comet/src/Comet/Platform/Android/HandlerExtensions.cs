using Microsoft.Maui.Handlers;
using Comet.Android.Controls;
using Microsoft.Maui.Graphics;
using Android.Content;
using AView = Android.Views.View;
using MotionEvent = Android.Views.MotionEvent;
using MotionEventActions = Android.Views.MotionEventActions;
using DragEvent = Android.Views.DragEvent;
using DragAction = Android.Views.DragAction;
namespace Comet;

public static partial class HandlerExtensions
{
	public static void AddGesture(this IViewHandler handler, Gesture gesture)
	{
		if (gesture is DragGesture dragGesture)
		{
			var nativeView = handler.PlatformView as AView;
			var view = handler.VirtualView as View;
			if (nativeView is null)
				return;
			nativeView.LongClick += (s, e) =>
			{
				if (!dragGesture.CanDrag)
					return;
				var data = dragGesture.DragStarting?.Invoke(view);
				dragGesture.DragStartingCommand?.Execute(dragGesture.DragStartingCommandParameter);

				var text = data?.ToString() ?? string.Empty;
				var clipData = ClipData.NewPlainText("drag", text);
				var shadow = new AView.DragShadowBuilder(nativeView);
				if (OperatingSystem.IsAndroidVersionAtLeast(24))
					nativeView.StartDragAndDrop(clipData, shadow, null, 0);
				else
#pragma warning disable CA1422
					nativeView.StartDrag(clipData, shadow, null, 0);
#pragma warning restore CA1422
			};
			gesture.PlatformGesture = nativeView;
			return;
		}

		if (gesture is DropGesture dropGesture)
		{
			var nativeView = handler.PlatformView as AView;
			var view = handler.VirtualView as View;
			if (nativeView is null)
				return;
			nativeView.SetOnDragListener(new CometDragListener(dropGesture, view));
			gesture.PlatformGesture = nativeView;
			return;
		}

		if (gesture is PointerGesture pointerGesture)
		{
			var nativeView = handler.PlatformView as AView;
			var view = handler.VirtualView as View;
			if (nativeView is null)
				return;
			nativeView.Hover += (s, e) =>
			{
				var point = new Point(e.Event.GetX(), e.Event.GetY());
				switch (e.Event.Action)
				{
					case MotionEventActions.HoverEnter:
						pointerGesture.PointerEntered?.Invoke(view, point);
						pointerGesture.PointerEnteredCommand?.Execute(point);
						break;
					case MotionEventActions.HoverMove:
						pointerGesture.PointerMoved?.Invoke(view, point);
						pointerGesture.PointerMovedCommand?.Execute(point);
						break;
					case MotionEventActions.HoverExit:
						pointerGesture.PointerExited?.Invoke(view, point);
						pointerGesture.PointerExitedCommand?.Execute(point);
						break;
				}
				e.Handled = true;
			};
			gesture.PlatformGesture = nativeView;
			return;
		}

		var gl = handler.GetGestureListener(true);
		gl.AddGesture(gesture);
	}

	public static void RemoveGesture(this IViewHandler handler, Gesture gesture)
	{
		var nativeView = handler.PlatformView as AView;

		if (gesture is DragGesture)
		{
			if (nativeView is not null)
				nativeView.LongClickable = false;
			return;
		}

		if (gesture is PointerGesture)
		{
			// Hover events are cleared by removing all hover listeners
			// The native view will stop receiving hover events when no longer registered
			return;
		}

		if (gesture is DropGesture)
		{
			nativeView?.SetOnDragListener(null);
			return;
		}

		var gl = handler.GetGestureListener(false);
		gl?.RemoveGesture(gesture);
	}
	public static CometTouchGestureListener GetGestureListener(this IViewHandler handler, bool createIfNull)
	{
		var v = handler.VirtualView as View;
		if (v is null)
			return null;
		var gl = v.GetEnvironment<ObjectWrapper<CometTouchGestureListener>>(nameof(CometTouchGestureListener), false)?.Object;
		if (gl is null && createIfNull)
			v.SetEnvironment(nameof(CometTouchGestureListener), new ObjectWrapper<CometTouchGestureListener> { Object = gl = new CometTouchGestureListener(handler.PlatformView as AView, v) }, false);
		return gl;
	}
	public static bool IsComplete(this MotionEvent e)
	{
		switch (e.Action)
		{
			case MotionEventActions.Cancel:
			case MotionEventActions.Outside:
			case MotionEventActions.PointerUp:
			case MotionEventActions.Up:
				return true;
			default:
				return false;
		}
	}

	internal class ObjectWrapper<T>
	{
		public T Object { get; set; }
	}

	class CometDragListener : Java.Lang.Object, AView.IOnDragListener
	{
		readonly DropGesture _gesture;
		readonly View _view;

		public CometDragListener(DropGesture gesture, View view)
		{
			_gesture = gesture;
			_view = view;
		}

		public bool OnDrag(AView v, DragEvent e)
		{
			if (!_gesture.AllowDrop)
				return false;

			switch (e.Action)
			{
				case DragAction.Entered:
					var accepted = _gesture.DragOver?.Invoke(_view, null) ?? true;
					_gesture.DragOverCommand?.Execute(null);
					return accepted;
				case DragAction.Location:
					break;
				case DragAction.Drop:
					var clipData = e.ClipData;
					object data = clipData?.ItemCount > 0 ? clipData.GetItemAt(0)?.Text : null;
					_gesture.Drop?.Invoke(_view, data);
					_gesture.DropCommand?.Execute(_gesture.DropCommandParameter ?? data);
					return true;
				case DragAction.Exited:
					_gesture.DragLeave?.Invoke(_view);
					_gesture.DragLeaveCommand?.Execute(null);
					break;
				case DragAction.Ended:
					break;
			}
			return true;
		}
	}
}


