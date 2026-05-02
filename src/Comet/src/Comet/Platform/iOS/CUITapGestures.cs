using System;
using System.Linq;
using Foundation;
using Microsoft.Maui.Graphics;
using UIKit;
namespace Comet.iOS
{
	public class CUITapGesture : UITapGestureRecognizer
	{
		public CUITapGesture(TapGesture gesture) : base(() => gesture.Invoke())
		{
			gesture.PlatformGesture = this;
		}
		public override UIGestureRecognizerState State { get => base.State; set => base.State = value; }
	}

	public class CUILongPressGesture : UILongPressGestureRecognizer
	{
		readonly LongPressGesture _gesture;
		public CUILongPressGesture(LongPressGesture gesture) : base(() => gesture.Invoke())
		{
			_gesture = gesture;
			gesture.PlatformGesture = this;
			MinimumPressDuration = gesture.MinimumPressDuration;
		}
	}

	public class CUIPanGesture : UIPanGestureRecognizer
	{
		readonly PanGesture _gesture;
		public CUIPanGesture(PanGesture gesture)
		{
			_gesture = gesture;
			gesture.PlatformGesture = this;
			AddTarget(() =>
			{
				var translation = TranslationInView(View);
				_gesture.TotalX = translation.X;
				_gesture.TotalY = translation.Y;
				_gesture.Status = State switch
				{
					UIGestureRecognizerState.Began => GestureStatus.Started,
					UIGestureRecognizerState.Changed => GestureStatus.Running,
					UIGestureRecognizerState.Ended => GestureStatus.Completed,
					UIGestureRecognizerState.Cancelled => GestureStatus.Canceled,
					_ => GestureStatus.Running
				};
				_gesture.Invoke();
			});
		}
	}

	public class CUIPinchGesture : UIPinchGestureRecognizer
	{
		readonly PinchGesture _gesture;
		public CUIPinchGesture(PinchGesture gesture)
		{
			_gesture = gesture;
			gesture.PlatformGesture = this;
			AddTarget(() =>
			{
				_gesture.Scale = Scale;
				_gesture.Status = State switch
				{
					UIGestureRecognizerState.Began => GestureStatus.Started,
					UIGestureRecognizerState.Changed => GestureStatus.Running,
					UIGestureRecognizerState.Ended => GestureStatus.Completed,
					UIGestureRecognizerState.Cancelled => GestureStatus.Canceled,
					_ => GestureStatus.Running
				};
				_gesture.Invoke();
			});
		}
	}

	public class CUISwipeGesture : UISwipeGestureRecognizer
	{
		public CUISwipeGesture(SwipeGesture gesture) : base(() => gesture.Invoke())
		{
			gesture.PlatformGesture = this;
			Direction = gesture.Direction switch
			{
				SwipeDirection.Left => UISwipeGestureRecognizerDirection.Left,
				SwipeDirection.Right => UISwipeGestureRecognizerDirection.Right,
				SwipeDirection.Up => UISwipeGestureRecognizerDirection.Up,
				SwipeDirection.Down => UISwipeGestureRecognizerDirection.Down,
				_ => UISwipeGestureRecognizerDirection.Left
			};
		}
	}

	public class CUIHoverGesture : UIHoverGestureRecognizer
	{
		readonly PointerGesture _gesture;
		readonly WeakReference<View> _viewRef;

		public CUIHoverGesture(PointerGesture gesture, View view) : base(() => { })
		{
			_gesture = gesture;
			_viewRef = new WeakReference<View>(view);
			gesture.PlatformGesture = this;
			AddTarget(() =>
			{
				if (!_viewRef.TryGetTarget(out var cometView))
					return;
				var location = LocationInView(View);
				var point = new Point(location.X, location.Y);
				switch (State)
				{
					case UIGestureRecognizerState.Began:
						_gesture.PointerEntered?.Invoke(cometView, point);
						_gesture.PointerEnteredCommand?.Execute(point);
						break;
					case UIGestureRecognizerState.Changed:
						_gesture.PointerMoved?.Invoke(cometView, point);
						_gesture.PointerMovedCommand?.Execute(point);
						break;
					case UIGestureRecognizerState.Ended:
					case UIGestureRecognizerState.Cancelled:
						_gesture.PointerExited?.Invoke(cometView, point);
						_gesture.PointerExitedCommand?.Execute(point);
						break;
				}
			});
		}
	}

	class CUIDragInteractionDelegate : UIDragInteractionDelegate
	{
		readonly DragGesture _gesture;
		readonly WeakReference<View> _viewRef;

		public CUIDragInteractionDelegate(DragGesture gesture, View view)
		{
			_gesture = gesture;
			_viewRef = new WeakReference<View>(view);
		}

		public override UIDragItem[] GetItemsForBeginningSession(UIDragInteraction interaction, IUIDragSession session)
		{
			if (!_gesture.CanDrag)
				return Array.Empty<UIDragItem>();

			_viewRef.TryGetTarget(out var view);
			var data = _gesture.DragStarting?.Invoke(view);
			_gesture.DragStartingCommand?.Execute(_gesture.DragStartingCommandParameter);

			var itemProvider = new NSItemProvider(new NSString(data?.ToString() ?? string.Empty));
			return new[] { new UIDragItem(itemProvider) };
		}

		public override void SessionDidEnd(UIDragInteraction interaction, IUIDragSession session, UIDropOperation operation)
		{
			_viewRef.TryGetTarget(out var view);
			_gesture.DropCompleted?.Invoke(view);
			_gesture.DropCompletedCommand?.Execute(_gesture.DropCompletedCommandParameter);
		}
	}

	class CUIDropInteractionDelegate : UIDropInteractionDelegate
	{
		readonly DropGesture _gesture;
		readonly WeakReference<View> _viewRef;

		public CUIDropInteractionDelegate(DropGesture gesture, View view)
		{
			_gesture = gesture;
			_viewRef = new WeakReference<View>(view);
		}

		public override bool CanHandleSession(UIDropInteraction interaction, IUIDropSession session) => _gesture.AllowDrop;

		public override UIDropProposal SessionDidUpdate(UIDropInteraction interaction, IUIDropSession session)
		{
			_viewRef.TryGetTarget(out var view);
			var accepted = _gesture.DragOver?.Invoke(view, null) ?? true;
			_gesture.DragOverCommand?.Execute(null);
			return new UIDropProposal(accepted ? UIDropOperation.Copy : UIDropOperation.Cancel);
		}

		public override void PerformDrop(UIDropInteraction interaction, IUIDropSession session)
		{
			_viewRef.TryGetTarget(out var view);

			// Try to load string data from the first drag item before invoking callbacks
			var items = session.Items;
			if (items is not null && items.Length > 0)
			{
				items[0].ItemProvider.LoadObject(new ObjCRuntime.Class(typeof(NSString)), (data, error) =>
				{
					CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
					{
						var dropData = data is NSString str ? str.ToString() : null;
						_gesture.Drop?.Invoke(view, dropData);
						_gesture.DropCommand?.Execute(_gesture.DropCommandParameter);
					});
				});
			}
			else
			{
				// No items to load — invoke immediately with null data
				_gesture.Drop?.Invoke(view, null);
				_gesture.DropCommand?.Execute(_gesture.DropCommandParameter);
			}
		}

		public override void SessionDidExit(UIDropInteraction interaction, IUIDropSession session)
		{
			_viewRef.TryGetTarget(out var view);
			_gesture.DragLeave?.Invoke(view);
			_gesture.DragLeaveCommand?.Execute(null);
		}
	}
}
