#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Maui.Controls;
using MauiDragEventArgs = Microsoft.Maui.Controls.DragEventArgs;
using MauiPoint = Microsoft.Maui.Graphics.Point;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Manages gesture recognizer attachment for WPF FrameworkElements.
	/// Handles Tap, Pointer, Pan, Swipe, Drag, Drop, Pinch, and LongPress gestures.
	/// </summary>
	public static class GestureManager
	{
		/// <summary>
		/// Private data format carrying the MAUI <see cref="DataPackage"/> from a
		/// <see cref="DragGestureRecognizer"/> to a <see cref="DropGestureRecognizer"/>.
		/// </summary>
		const string DataPackageFormat = "Microsoft.Maui.Controls.DataPackage";

		/// <summary>
		/// Attached property storing gesture cleanup handlers. Uses a DependencyProperty
		/// instead of FrameworkElement.Tag to avoid conflicts with other WPF code.
		/// </summary>
		static readonly DependencyProperty GestureCleanupProperty =
			DependencyProperty.RegisterAttached(
				"GestureCleanup",
				typeof(List<GestureCleanup>),
				typeof(GestureManager),
				new PropertyMetadata(null));

		static int s_nextPanGestureId;

		public static void SetupGestures(FrameworkElement platformView, IView virtualView)
		{
			if (virtualView is not View mauiView)
				return;

			// Remove old gesture handlers
			ClearManagedGestures(platformView);

			foreach (var recognizer in mauiView.GestureRecognizers)
			{
				switch (recognizer)
				{
					case TapGestureRecognizer tap:
						AddTapGesture(platformView, tap);
						break;
					case PointerGestureRecognizer pointer:
						AddPointerGesture(platformView, pointer);
						break;
					case PanGestureRecognizer pan:
						AddPanGesture(platformView, pan);
						break;
					case SwipeGestureRecognizer swipe:
						AddSwipeGesture(platformView, swipe);
						break;
					case DragGestureRecognizer drag:
						AddDragGesture(platformView, drag);
						break;
					case DropGestureRecognizer drop:
						AddDropGesture(platformView, drop);
						break;
					case PinchGestureRecognizer pinch:
						AddPinchGesture(platformView, pinch);
						break;
					case LongPressGestureRecognizer longPress:
						AddLongPressGesture(platformView, longPress);
						break;
				}
			}
		}

		static void ClearManagedGestures(FrameworkElement view)
		{
			if (view.GetValue(GestureCleanupProperty) is List<GestureCleanup> cleanups)
			{
				foreach (var cleanup in cleanups)
					cleanup.Detach();
				cleanups.Clear();
			}
		}

		static List<GestureCleanup> GetCleanups(FrameworkElement view)
		{
			if (view.GetValue(GestureCleanupProperty) is not List<GestureCleanup> list)
			{
				list = new List<GestureCleanup>();
				view.SetValue(GestureCleanupProperty, list);
			}
			return list;
		}

		static void EnsureHitTestable(FrameworkElement view)
		{
			if (view is System.Windows.Controls.Control ctrl && ctrl.Background == null)
				ctrl.Background = System.Windows.Media.Brushes.Transparent;
			else if (view is System.Windows.Controls.Panel pnl && pnl.Background == null)
				pnl.Background = System.Windows.Media.Brushes.Transparent;
		}

		#region Tap Gesture

		static void AddTapGesture(FrameworkElement view, TapGestureRecognizer tap)
		{
			MouseButtonEventHandler handler = (s, e) =>
			{
				// SendTapped executes Command and raises Tapped; it requires the owning view.
				var parent = FindParentView(tap);
				if (e.ClickCount >= tap.NumberOfTapsRequired && parent != null)
					tap.SendTapped(parent, CreateGetPosition(view, e.MouseDevice));
			};

			view.MouseLeftButtonUp += handler;
			EnsureHitTestable(view);
			GetCleanups(view).Add(new GestureCleanup(() => view.MouseLeftButtonUp -= handler));
		}

		#endregion

		#region Pointer Gesture

		static void AddPointerGesture(FrameworkElement view, PointerGestureRecognizer pointer)
		{
			// The PointerGestureRecognizer Send* methods require the owning view; a recognizer
			// that has been detached from its view has nothing to report against.
			MouseEventHandler enterHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				if (parent != null)
					pointer.SendPointerEntered(parent, CreateGetPosition(view, e.MouseDevice));
			};

			MouseEventHandler leaveHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				if (parent != null)
					pointer.SendPointerExited(parent, CreateGetPosition(view, e.MouseDevice));
			};

			MouseEventHandler moveHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				if (parent != null)
					pointer.SendPointerMoved(parent, CreateGetPosition(view, e.MouseDevice));
			};

			MouseButtonEventHandler pressedHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				if (parent != null)
					pointer.SendPointerPressed(parent, CreateGetPosition(view, e.MouseDevice), button: ToButtonsMask(e.ChangedButton));
			};

			MouseButtonEventHandler releasedHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				if (parent != null)
					pointer.SendPointerReleased(parent, CreateGetPosition(view, e.MouseDevice), button: ToButtonsMask(e.ChangedButton));
			};

			view.MouseEnter += enterHandler;
			view.MouseLeave += leaveHandler;
			view.MouseMove += moveHandler;
			view.MouseDown += pressedHandler;
			view.MouseUp += releasedHandler;

			var cleanups = GetCleanups(view);
			cleanups.Add(new GestureCleanup(() =>
			{
				view.MouseEnter -= enterHandler;
				view.MouseLeave -= leaveHandler;
				view.MouseMove -= moveHandler;
				view.MouseDown -= pressedHandler;
				view.MouseUp -= releasedHandler;
			}));
		}

		static ButtonsMask ToButtonsMask(MouseButton button) =>
			button == MouseButton.Right ? ButtonsMask.Secondary : ButtonsMask.Primary;

		#endregion

		#region Pan Gesture

		static void AddPanGesture(FrameworkElement view, PanGestureRecognizer pan)
		{
			IPanGestureController controller = pan;
			System.Windows.Point? startPoint = null;
			bool isPanning = false;
			int gestureId = 0;

			MouseButtonEventHandler downHandler = (s, e) =>
			{
				startPoint = e.GetPosition(view);
				isPanning = false;
				view.CaptureMouse();
			};

			MouseEventHandler moveHandler = (s, e) =>
			{
				var parent = FindParentView(pan);
				if (startPoint == null || parent == null || e.LeftButton != MouseButtonState.Pressed) return;

				var current = e.GetPosition(view);
				double totalX = current.X - startPoint.Value.X;
				double totalY = current.Y - startPoint.Value.Y;

				if (!isPanning)
				{
					isPanning = true;
					gestureId = System.Threading.Interlocked.Increment(ref s_nextPanGestureId);
					controller.SendPanStarted(parent, gestureId);
				}

				controller.SendPan(parent, totalX, totalY, gestureId);
			};

			MouseButtonEventHandler upHandler = (s, e) =>
			{
				var parent = FindParentView(pan);
				var wasPanning = isPanning;
				// Clear state first: releasing capture raises LostMouseCapture synchronously.
				startPoint = null;
				isPanning = false;
				view.ReleaseMouseCapture();
				if (wasPanning && parent != null)
					controller.SendPanCompleted(parent, gestureId);
			};

			MouseEventHandler lostCaptureHandler = (s, e) =>
			{
				// Capture can be taken away mid-pan (e.g. by a dialog or Alt+Tab).
				var parent = FindParentView(pan);
				if (isPanning && parent != null)
					controller.SendPanCanceled(parent, gestureId);
				startPoint = null;
				isPanning = false;
			};

			view.MouseLeftButtonDown += downHandler;
			view.MouseMove += moveHandler;
			view.MouseLeftButtonUp += upHandler;
			view.LostMouseCapture += lostCaptureHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				view.MouseLeftButtonDown -= downHandler;
				view.MouseMove -= moveHandler;
				view.MouseLeftButtonUp -= upHandler;
				view.LostMouseCapture -= lostCaptureHandler;
			}));
		}

		#endregion

		#region Swipe Gesture

		static void AddSwipeGesture(FrameworkElement view, SwipeGestureRecognizer swipe)
		{
			System.Windows.Point? startPoint = null;

			MouseButtonEventHandler downHandler = (s, e) =>
			{
				startPoint = e.GetPosition(view);
			};

			MouseButtonEventHandler upHandler = (s, e) =>
			{
				if (startPoint == null) return;
				var endPoint = e.GetPosition(view);
				double deltaX = endPoint.X - startPoint.Value.X;
				double deltaY = endPoint.Y - startPoint.Value.Y;

				SwipeDirection? direction = null;
				double threshold = swipe.Threshold > 0 ? swipe.Threshold : 50;

				if (Math.Abs(deltaX) > Math.Abs(deltaY))
				{
					if (deltaX > threshold && swipe.Direction.HasFlag(SwipeDirection.Right))
						direction = SwipeDirection.Right;
					else if (deltaX < -threshold && swipe.Direction.HasFlag(SwipeDirection.Left))
						direction = SwipeDirection.Left;
				}
				else
				{
					if (deltaY > threshold && swipe.Direction.HasFlag(SwipeDirection.Down))
						direction = SwipeDirection.Down;
					else if (deltaY < -threshold && swipe.Direction.HasFlag(SwipeDirection.Up))
						direction = SwipeDirection.Up;
				}

				// SendSwiped executes Command and raises Swiped.
				var parent = FindParentView(swipe);
				if (direction != null && parent != null)
					swipe.SendSwiped(parent, direction.Value);

				startPoint = null;
			};

			view.MouseLeftButtonDown += downHandler;
			view.MouseLeftButtonUp += upHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				view.MouseLeftButtonDown -= downHandler;
				view.MouseLeftButtonUp -= upHandler;
			}));
		}

		#endregion

		#region Drag & Drop

		static void AddDragGesture(FrameworkElement view, DragGestureRecognizer drag)
		{
			MouseEventHandler moveHandler = (s, e) =>
			{
				var parent = FindParentView(drag);
				if (e.LeftButton != MouseButtonState.Pressed || !drag.CanDrag || parent == null) return;

				// SendDragStarting executes DragStartingCommand, raises DragStarting and fills the
				// DataPackage; a canceled or handled drag must not start a WPF drag operation.
				var args = drag.SendDragStarting(parent, CreateGetPosition(view, e.MouseDevice));
				if (args.Cancel || args.Handled) return;

				var data = new DataObject();
				data.SetData(DataPackageFormat, args.Data);
				var text = args.Data.Text;
				if (!string.IsNullOrEmpty(text))
					data.SetText(text);

				DragDrop.DoDragDrop(view, data, DragDropEffects.Copy | DragDropEffects.Move);
				drag.SendDropCompleted(new DropCompletedEventArgs());
			};

			view.MouseMove += moveHandler;
			GetCleanups(view).Add(new GestureCleanup(() => view.MouseMove -= moveHandler));
		}

		static void AddDropGesture(FrameworkElement view, DropGestureRecognizer drop)
		{
			// The operation negotiated by the last DragOver, reported back to the source on Drop.
			var negotiated = DragDropEffects.None;

			view.AllowDrop = drop.AllowDrop;

			// WPF only raises DragOver/Drop on elements whose AllowDrop is set, so a binding that
			// enables dropping later must reach the platform view.
			System.ComponentModel.PropertyChangedEventHandler allowDropHandler = (s, e) =>
			{
				if (e.PropertyName == DropGestureRecognizer.AllowDropProperty.PropertyName)
					view.AllowDrop = drop.AllowDrop;
			};
			drop.PropertyChanged += allowDropHandler;

			DragEventHandler dragOverHandler = (s, e) =>
			{
				e.Handled = true;
				var args = new MauiDragEventArgs(GetDataPackage(e.Data));
				drop.SendDragOver(args);
				negotiated = drop.AllowDrop
					? NegotiateEffects(args.AcceptedOperation, e.AllowedEffects)
					: DragDropEffects.None;
				e.Effects = negotiated;
			};

			DragEventHandler dragLeaveHandler = (s, e) =>
			{
				drop.SendDragLeave(new MauiDragEventArgs(GetDataPackage(e.Data)));
			};

			DragEventHandler dropHandler = async (s, e) =>
			{
				e.Handled = true;
				// Must be set before the first await: DoDragDrop returns this to the drag source.
				e.Effects = negotiated;
				try
				{
					await drop.SendDrop(new DropEventArgs(GetDataPackage(e.Data).View));
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[GestureManager] Drop handling failed: {ex}");
				}
			};

			view.DragOver += dragOverHandler;
			view.DragLeave += dragLeaveHandler;
			view.Drop += dropHandler;

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				drop.PropertyChanged -= allowDropHandler;
				view.DragOver -= dragOverHandler;
				view.DragLeave -= dragLeaveHandler;
				view.Drop -= dropHandler;
				view.ClearValue(UIElement.AllowDropProperty);
			}));
		}

		/// <summary>
		/// Returns the MAUI DataPackage of a drag started by a <see cref="DragGestureRecognizer"/>,
		/// or wraps the text of a drag that came from outside MAUI (another app, Explorer).
		/// </summary>
		static DataPackage GetDataPackage(IDataObject data)
		{
			if (data.GetData(DataPackageFormat) is DataPackage package)
				return package;

			var external = new DataPackage();
			if (data.GetData(DataFormats.UnicodeText) is string text)
				external.Text = text;
			return external;
		}

		/// <summary>
		/// Maps the operation a MAUI <c>DragOver</c> handler accepted onto WPF effects, limited to
		/// what the drag source allows (a move-only source cannot be answered with a copy).
		/// </summary>
		internal static DragDropEffects NegotiateEffects(DataPackageOperation accepted, DragDropEffects allowed) =>
			ToDragDropEffects(accepted & ToDataPackageOperation(allowed));

		/// <summary>
		/// Maps MAUI's <see cref="DataPackageOperation"/> flags onto WPF <see cref="DragDropEffects"/>,
		/// preserving Copy, Move and Copy | Move.
		/// </summary>
		internal static DragDropEffects ToDragDropEffects(DataPackageOperation operation)
		{
			var effects = DragDropEffects.None;
			if (operation.HasFlag(DataPackageOperation.Copy))
				effects |= DragDropEffects.Copy;
			if (operation.HasFlag(DataPackageOperation.Move))
				effects |= DragDropEffects.Move;
			return effects;
		}

		/// <summary>
		/// Maps WPF <see cref="DragDropEffects"/> onto MAUI's <see cref="DataPackageOperation"/>.
		/// Link and Scroll have no MAUI equivalent and are dropped.
		/// </summary>
		internal static DataPackageOperation ToDataPackageOperation(DragDropEffects effects)
		{
			var operation = DataPackageOperation.None;
			if (effects.HasFlag(DragDropEffects.Copy))
				operation |= DataPackageOperation.Copy;
			if (effects.HasFlag(DragDropEffects.Move))
				operation |= DataPackageOperation.Move;
			return operation;
		}

		#endregion

		#region Pinch Gesture

		static void AddPinchGesture(FrameworkElement view, PinchGestureRecognizer pinch)
		{
			// WPF doesn't have native pinch — map Ctrl+MouseWheel to zoom
			IPinchGestureController controller = pinch;
			bool isPinching = false;

			MouseWheelEventHandler wheelHandler = (s, e) =>
			{
				var parent = FindParentView(pinch);
				if ((System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Control) == 0 || parent == null)
					return;

				var scaleOrigin = GetScaleOrigin(view, e);
				if (!isPinching)
				{
					isPinching = true;
					controller.SendPinchStarted(parent, scaleOrigin);
				}

				// PinchGestureUpdatedEventArgs.Scale is relative to the previous update, not cumulative.
				controller.SendPinch(parent, e.Delta > 0 ? 1.1 : 0.9, scaleOrigin);
				e.Handled = true;
			};

			MouseEventHandler leaveHandler = (s, e) =>
			{
				var parent = FindParentView(pinch);
				if (isPinching && parent != null)
					controller.SendPinchEnded(parent);
				isPinching = false;
			};

			view.MouseWheel += wheelHandler;
			view.MouseLeave += leaveHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				view.MouseWheel -= wheelHandler;
				view.MouseLeave -= leaveHandler;
			}));
		}

		/// <summary>
		/// MAUI pinch scale origins are proportional (0..1) to the recognizing view's size.
		/// </summary>
		static MauiPoint GetScaleOrigin(FrameworkElement view, MouseEventArgs e)
		{
			var position = e.GetPosition(view);
			return new MauiPoint(
				view.ActualWidth > 0 ? position.X / view.ActualWidth : 0.5,
				view.ActualHeight > 0 ? position.Y / view.ActualHeight : 0.5);
		}

		#endregion

		#region LongPress Gesture

		/// <summary>
		/// Maps a held left mouse button onto <see cref="LongPressGestureRecognizer"/>, following
		/// the MAUI platform managers: Started once MinimumPressDuration elapses, Running while the
		/// pointer moves within AllowableMovement, then LongPressed plus Completed on release, or
		/// Canceled if the pointer moves too far or leaves the view.
		/// </summary>
		static void AddLongPressGesture(FrameworkElement view, LongPressGestureRecognizer longPress)
		{
			DispatcherTimer? timer = null;
			System.Windows.Point? startPoint = null;
			bool isRecognized = false;

			void Reset()
			{
				timer?.Stop();
				timer = null;
				startPoint = null;
				isRecognized = false;
			}

			void Cancel(MouseDevice mouse)
			{
				var parent = FindParentView(longPress);
				if (isRecognized && parent != null)
					longPress.SendLongPressing(parent, GestureStatus.Canceled, CreateGetPosition(view, mouse));
				Reset();
			}

			MouseButtonEventHandler downHandler = (s, e) =>
			{
				Reset();
				startPoint = e.GetPosition(view);
				var mouse = e.MouseDevice;
				var pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(longPress.MinimumPressDuration) };
				pressTimer.Tick += (_, _) =>
				{
					pressTimer.Stop();
					var parent = FindParentView(longPress);
					if (parent == null)
					{
						Reset();
						return;
					}

					isRecognized = true;
					longPress.SendLongPressing(parent, GestureStatus.Started, CreateGetPosition(view, mouse));
				};
				timer = pressTimer;
				pressTimer.Start();
			};

			MouseEventHandler moveHandler = (s, e) =>
			{
				if (startPoint == null) return;

				var distance = (e.GetPosition(view) - startPoint.Value).Length;
				var parent = FindParentView(longPress);
				if (distance > longPress.AllowableMovement)
					Cancel(e.MouseDevice);
				else if (isRecognized && parent != null)
					longPress.SendLongPressing(parent, GestureStatus.Running, CreateGetPosition(view, e.MouseDevice));
			};

			MouseButtonEventHandler upHandler = (s, e) =>
			{
				var parent = FindParentView(longPress);
				if (isRecognized && parent != null)
				{
					// SendLongPressed executes Command and raises LongPressed.
					var getPosition = CreateGetPosition(view, e.MouseDevice);
					longPress.SendLongPressed(parent, getPosition);
					longPress.SendLongPressing(parent, GestureStatus.Completed, getPosition);
				}
				Reset();
			};

			MouseEventHandler leaveHandler = (s, e) => Cancel(e.MouseDevice);

			view.MouseLeftButtonDown += downHandler;
			view.MouseMove += moveHandler;
			view.MouseLeftButtonUp += upHandler;
			view.MouseLeave += leaveHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				Reset();
				view.MouseLeftButtonDown -= downHandler;
				view.MouseMove -= moveHandler;
				view.MouseLeftButtonUp -= upHandler;
				view.MouseLeave -= leaveHandler;
			}));
		}

		#endregion

		/// <summary>
		/// Builds the <c>getPosition</c> callback MAUI gesture event args expose. The pointer position
		/// is captured now, so args queried later still report where the gesture happened.
		/// </summary>
		static Func<IElement?, MauiPoint?> CreateGetPosition(FrameworkElement view, MouseDevice mouse)
		{
			var positionInView = mouse.GetPosition(view);
			return relativeTo => ResolvePosition(view, positionInView, relativeTo);
		}

		/// <summary>
		/// Converts a position in <paramref name="view"/>'s coordinates for MAUI's
		/// <c>GetPosition(relativeTo)</c>.
		/// </summary>
		/// <remarks>
		/// A <see langword="null"/> <paramref name="relativeTo"/> yields window coordinates (relative to
		/// the root of the view's visual tree, i.e. the WPF window's client area) for taps, pointer and
		/// long-press events alike. That is what MAUI 11 returns on Windows
		/// (<c>TappedRoutedEventArgs.GetPosition(null)</c>) and iOS/Mac Catalyst
		/// (<c>UIGestureRecognizer.LocationInView(null)</c>), and the documented contract of
		/// <see cref="LongPressedEventArgs.GetPosition"/>. Any other element is resolved through its
		/// handler's platform view; an element without one, or in an unrelated visual tree that is not
		/// on screen, yields <see langword="null"/>.
		/// </remarks>
		internal static MauiPoint? ResolvePosition(UIElement view, System.Windows.Point positionInView, IElement? relativeTo)
		{
			var target = relativeTo is null
				? GetVisualRoot(view)
				: relativeTo.Handler?.PlatformView as UIElement;
			if (target is null)
				return null;

			if (ReferenceEquals(target, view))
				return new MauiPoint(positionInView.X, positionInView.Y);

			if (view.FindCommonVisualAncestor(target) is not null)
			{
				var translated = view.TranslatePoint(positionInView, target);
				return new MauiPoint(translated.X, translated.Y);
			}

			// Another window (e.g. a modal page) is reached through screen coordinates.
			if (PresentationSource.FromVisual(view) is null || PresentationSource.FromVisual(target) is null)
				return null;

			var onTarget = target.PointFromScreen(view.PointToScreen(positionInView));
			return new MauiPoint(onTarget.X, onTarget.Y);
		}

		static UIElement GetVisualRoot(UIElement view)
		{
			DependencyObject root = view;
			var parent = VisualTreeHelper.GetParent(root);
			while (parent is not null)
			{
				root = parent;
				parent = VisualTreeHelper.GetParent(root);
			}
			return root as UIElement ?? view;
		}

		static View? FindParentView(IGestureRecognizer recognizer)
		{
			if (recognizer is Element element)
			{
				var current = element.Parent;
				while (current != null)
				{
					if (current is View view)
						return view;
					current = current.Parent;
				}
			}
			return null;
		}

		class GestureCleanup
		{
			readonly Action _detach;
			public GestureCleanup(Action detach) => _detach = detach;
			public void Detach() => _detach();
		}
	}
}
