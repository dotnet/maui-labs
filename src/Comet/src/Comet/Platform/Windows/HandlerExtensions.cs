using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Comet;

public static partial class HandlerExtensions
{
	public static void AddGesture(this IViewHandler handler, Gesture gesture)
	{
		if (handler.PlatformView is not UIElement nativeView)
			return;

		if (gesture is TapGesture tapGesture)
		{
			nativeView.Tapped += (s, e) => tapGesture.Invoke();
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is LongPressGesture longPressGesture)
		{
			nativeView.Holding += (s, e) =>
			{
				if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started)
					longPressGesture.Invoke();
			};
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is PanGesture panGesture)
		{
			nativeView.ManipulationMode |= ManipulationModes.TranslateX | ManipulationModes.TranslateY;
			nativeView.ManipulationStarted += (s, e) =>
			{
				panGesture.TotalX = 0;
				panGesture.TotalY = 0;
				panGesture.Status = GestureStatus.Started;
				panGesture.Invoke();
			};
			nativeView.ManipulationDelta += (s, e) =>
			{
				panGesture.TotalX = e.Cumulative.Translation.X;
				panGesture.TotalY = e.Cumulative.Translation.Y;
				panGesture.Status = GestureStatus.Running;
				panGesture.Invoke();
			};
			nativeView.ManipulationCompleted += (s, e) =>
			{
				panGesture.TotalX = e.Cumulative.Translation.X;
				panGesture.TotalY = e.Cumulative.Translation.Y;
				panGesture.Status = GestureStatus.Completed;
				panGesture.Invoke();
			};
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is PinchGesture pinchGesture)
		{
			nativeView.ManipulationMode |= ManipulationModes.Scale;
			nativeView.ManipulationDelta += (s, e) =>
			{
				pinchGesture.Scale = e.Cumulative.Scale;
				pinchGesture.Status = GestureStatus.Running;
				pinchGesture.Invoke();
			};
			nativeView.ManipulationCompleted += (s, e) =>
			{
				pinchGesture.Scale = e.Cumulative.Scale;
				pinchGesture.Status = GestureStatus.Completed;
				pinchGesture.Invoke();
			};
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is DragGesture dragGesture)
		{
			var view = handler.VirtualView as View;
			nativeView.CanDrag = dragGesture.CanDrag;
			nativeView.DragStarting += (s, e) =>
			{
				var data = dragGesture.DragStarting?.Invoke(view);
				dragGesture.DragStartingCommand?.Execute(dragGesture.DragStartingCommandParameter);
				if (data is not null)
					e.Data.SetText(data.ToString());
			};
			nativeView.DropCompleted += (s, e) =>
			{
				dragGesture.DropCompleted?.Invoke(view);
				dragGesture.DropCompletedCommand?.Execute(dragGesture.DropCompletedCommandParameter);
			};
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is DropGesture dropGesture)
		{
			var view = handler.VirtualView as View;
			nativeView.AllowDrop = dropGesture.AllowDrop;
			nativeView.DragOver += (s, e) =>
			{
				var accepted = dropGesture.DragOver?.Invoke(view, null) ?? true;
				dropGesture.DragOverCommand?.Execute(null);
				e.AcceptedOperation = accepted
					? global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
					: global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
			};
			nativeView.DragLeave += (s, e) =>
			{
				dropGesture.DragLeave?.Invoke(view);
				dropGesture.DragLeaveCommand?.Execute(null);
			};
			nativeView.Drop += async (s, e) =>
			{
				object data = null;
				if (e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
					data = await e.DataView.GetTextAsync();
				dropGesture.Drop?.Invoke(view, data);
				dropGesture.DropCommand?.Execute(dropGesture.DropCommandParameter ?? data);
			};
			gesture.PlatformGesture = nativeView;
		}
		else if (gesture is PointerGesture pointerGesture)
		{
			var view = handler.VirtualView as View;
			nativeView.PointerEntered += (s, e) =>
			{
				var pos = e.GetCurrentPoint(nativeView).Position;
				var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);
				pointerGesture.PointerEntered?.Invoke(view, point);
				pointerGesture.PointerEnteredCommand?.Execute(point);
			};
			nativeView.PointerMoved += (s, e) =>
			{
				var pos = e.GetCurrentPoint(nativeView).Position;
				var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);
				pointerGesture.PointerMoved?.Invoke(view, point);
				pointerGesture.PointerMovedCommand?.Execute(point);
			};
			nativeView.PointerExited += (s, e) =>
			{
				var pos = e.GetCurrentPoint(nativeView).Position;
				var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);
				pointerGesture.PointerExited?.Invoke(view, point);
				pointerGesture.PointerExitedCommand?.Execute(point);
			};
			nativeView.PointerPressed += (s, e) =>
			{
				var pos = e.GetCurrentPoint(nativeView).Position;
				var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);
				pointerGesture.PointerPressed?.Invoke(view, point);
				pointerGesture.PointerPressedCommand?.Execute(point);
			};
			nativeView.PointerReleased += (s, e) =>
			{
				var pos = e.GetCurrentPoint(nativeView).Position;
				var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);
				pointerGesture.PointerReleased?.Invoke(view, point);
				pointerGesture.PointerReleasedCommand?.Execute(point);
			};
			gesture.PlatformGesture = nativeView;
		}
	}

	public static void RemoveGesture(this IViewHandler handler, Gesture gesture)
	{
		// Windows gesture handlers are attached to the UIElement lifecycle
		// and will be cleaned up when the native view is disposed
	}
}
