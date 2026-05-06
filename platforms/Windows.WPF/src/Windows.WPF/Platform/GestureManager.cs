#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Manages gesture recognizer attachment for WPF FrameworkElements.
	/// Handles Tap, Pointer, Pan, Drag, Drop, and LongPress gestures.
	/// </summary>
	public static class GestureManager
	{
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
				}
			}

			// Check for LongPress via reflection (MAUI 10+)
			foreach (var recognizer in mauiView.GestureRecognizers)
			{
				if (recognizer.GetType().Name == "LongPressGestureRecognizer")
					AddLongPressGesture(platformView, recognizer);
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
				if (e.ClickCount >= tap.NumberOfTapsRequired)
				{
					// Use SendTapped which handles Command execution + Tapped event
					var sendTapped = typeof(TapGestureRecognizer).GetMethod(
						"SendTapped", BindingFlags.Instance | BindingFlags.NonPublic);
					if (sendTapped != null)
					{
						var parent = FindParentView(tap);
						var parameters = sendTapped.GetParameters();
						if (parameters.Length == 1)
							sendTapped.Invoke(tap, new object?[] { parent });
						else if (parameters.Length == 2)
							sendTapped.Invoke(tap, new object?[] { parent, null });
					}
					else
					{
						// Fallback: execute command directly
						tap.Command?.Execute(tap.CommandParameter);
					}
				}
			};

			view.MouseLeftButtonUp += handler;
			EnsureHitTestable(view);
			GetCleanups(view).Add(new GestureCleanup(() => view.MouseLeftButtonUp -= handler));
		}

		#endregion

		#region Pointer Gesture

		static void AddPointerGesture(FrameworkElement view, PointerGestureRecognizer pointer)
		{
			var sendEntered = typeof(PointerGestureRecognizer).GetMethod("SendPointerEntered", BindingFlags.Instance | BindingFlags.NonPublic);
			var sendExited = typeof(PointerGestureRecognizer).GetMethod("SendPointerExited", BindingFlags.Instance | BindingFlags.NonPublic);
			var sendMoved = typeof(PointerGestureRecognizer).GetMethod("SendPointerMoved", BindingFlags.Instance | BindingFlags.NonPublic);

			MouseEventHandler enterHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				sendEntered?.Invoke(pointer, new object?[] { parent, null, null, ButtonsMask.Primary });
			};

			MouseEventHandler leaveHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				sendExited?.Invoke(pointer, new object?[] { parent, null, null, ButtonsMask.Primary });
			};

			MouseEventHandler moveHandler = (s, e) =>
			{
				var parent = FindParentView(pointer);
				sendMoved?.Invoke(pointer, new object?[] { parent, null, null, ButtonsMask.Primary });
			};

			view.MouseEnter += enterHandler;
			view.MouseLeave += leaveHandler;
			view.MouseMove += moveHandler;

			var cleanups = GetCleanups(view);
			cleanups.Add(new GestureCleanup(() =>
			{
				view.MouseEnter -= enterHandler;
				view.MouseLeave -= leaveHandler;
				view.MouseMove -= moveHandler;
			}));
		}

		#endregion

		#region Pan Gesture

		static void AddPanGesture(FrameworkElement view, PanGestureRecognizer pan)
		{
			System.Windows.Point? startPoint = null;
			bool isPanning = false;

			MouseButtonEventHandler downHandler = (s, e) =>
			{
				startPoint = e.GetPosition(view);
				isPanning = false;
				view.CaptureMouse();
			};

			MouseEventHandler moveHandler = (s, e) =>
			{
				if (startPoint == null || e.LeftButton != MouseButtonState.Pressed) return;

				var current = e.GetPosition(view);
				double deltaX = current.X - startPoint.Value.X;
				double deltaY = current.Y - startPoint.Value.Y;

				if (!isPanning)
				{
					isPanning = true;
					// Send PanStarted
					InvokePanMethod(pan, "SendPanStarted", FindParentView(pan), 0, 0);
				}

				InvokePanMethod(pan, "SendPanRunning", FindParentView(pan), deltaX, deltaY);
			};

			MouseButtonEventHandler upHandler = (s, e) =>
			{
				if (isPanning)
				{
					var current = e.GetPosition(view);
					double deltaX = current.X - startPoint!.Value.X;
					double deltaY = current.Y - startPoint.Value.Y;
					InvokePanMethod(pan, "SendPanCompleted", FindParentView(pan), deltaX, deltaY);
				}
				startPoint = null;
				isPanning = false;
				view.ReleaseMouseCapture();
			};

			view.MouseLeftButtonDown += downHandler;
			view.MouseMove += moveHandler;
			view.MouseLeftButtonUp += upHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				view.MouseLeftButtonDown -= downHandler;
				view.MouseMove -= moveHandler;
				view.MouseLeftButtonUp -= upHandler;
			}));
		}

		static void InvokePanMethod(PanGestureRecognizer pan, string methodName, View? parent, double totalX, double totalY)
		{
			try
			{
				var method = typeof(PanGestureRecognizer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
				if (method != null)
				{
					var parms = method.GetParameters();
					if (parms.Length == 3)
						method.Invoke(pan, new object?[] { parent, totalX, totalY });
					else if (parms.Length == 4)
						method.Invoke(pan, new object?[] { parent, totalX, totalY, null });
				}
			}
			catch { }
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

				if (direction != null)
				{
					swipe.Command?.Execute(swipe.CommandParameter);
					try
					{
						var method = typeof(SwipeGestureRecognizer).GetMethod("SendSwiped",
							BindingFlags.Instance | BindingFlags.NonPublic);
						var parent = FindParentView(swipe);
						method?.Invoke(swipe, new object?[] { parent, direction.Value });
					}
					catch { }
				}

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
				if (e.LeftButton != MouseButtonState.Pressed) return;

				try
				{
					var data = new DataObject();
					var parent = FindParentView(drag);
					if (parent != null)
						data.SetData("MauiDragSource", parent);

					// Fire DragStarting
					var method = typeof(DragGestureRecognizer).GetMethod("SendDragStarting",
						BindingFlags.Instance | BindingFlags.NonPublic);
					method?.Invoke(drag, new object?[] { parent });

					DragDrop.DoDragDrop(view, data, DragDropEffects.Copy | DragDropEffects.Move);
				}
				catch { }
			};

			view.MouseMove += moveHandler;
			GetCleanups(view).Add(new GestureCleanup(() => view.MouseMove -= moveHandler));
		}

		static void AddDropGesture(FrameworkElement view, DropGestureRecognizer drop)
		{
			view.AllowDrop = true;

			DragEventHandler dragOverHandler = (s, e) =>
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
				try
				{
					var method = typeof(DropGestureRecognizer).GetMethod("SendDragOver",
						BindingFlags.Instance | BindingFlags.NonPublic);
					var parent = FindParentView(drop);
					method?.Invoke(drop, new object?[] { parent, null });
				}
				catch { }
			};

			DragEventHandler dropHandler = (s, e) =>
			{
				e.Handled = true;
				try
				{
					var method = typeof(DropGestureRecognizer).GetMethod("SendDrop",
						BindingFlags.Instance | BindingFlags.NonPublic);
					var parent = FindParentView(drop);
					method?.Invoke(drop, new object?[] { parent, null });
				}
				catch { }
			};

			view.DragOver += dragOverHandler;
			view.Drop += dropHandler;

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				view.DragOver -= dragOverHandler;
				view.Drop -= dropHandler;
			}));
		}

		#endregion

		#region Pinch Gesture

		static void AddPinchGesture(FrameworkElement view, PinchGestureRecognizer pinch)
		{
			// WPF doesn't have native pinch — map Ctrl+MouseWheel to zoom
			double currentScale = 1.0;
			bool isPinching = false;

			MouseWheelEventHandler wheelHandler = (s, e) =>
			{
				if ((System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Control) != 0)
				{
					var parent = FindParentView(pinch);
					if (!isPinching)
					{
						isPinching = true;
						try
						{
							var startMethod = typeof(PinchGestureRecognizer).GetMethod("SendPinchStarted",
								BindingFlags.Instance | BindingFlags.NonPublic);
							startMethod?.Invoke(pinch, new object?[] { parent, null });
						}
						catch { }
					}

					double delta = e.Delta > 0 ? 1.1 : 0.9;
					currentScale *= delta;

					try
					{
						var updateMethod = typeof(PinchGestureRecognizer).GetMethod("SendPinch",
							BindingFlags.Instance | BindingFlags.NonPublic);
						updateMethod?.Invoke(pinch, new object?[] { parent, currentScale, null, GestureStatus.Running });
					}
					catch { }

					e.Handled = true;
				}
			};

			MouseEventHandler leaveHandler = (s, e) =>
			{
				if (isPinching)
				{
					isPinching = false;
					currentScale = 1.0;
					try
					{
						var endMethod = typeof(PinchGestureRecognizer).GetMethod("SendPinchEnded",
							BindingFlags.Instance | BindingFlags.NonPublic);
						var parent = FindParentView(pinch);
						endMethod?.Invoke(pinch, new object?[] { parent });
					}
					catch { }
				}
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

		#endregion

		#region LongPress Gesture

		static void AddLongPressGesture(FrameworkElement view, IGestureRecognizer recognizer)
		{
			DispatcherTimer? timer = null;

			MouseButtonEventHandler downHandler = (s, e) =>
			{
				timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
				timer.Tick += (_, _) =>
				{
					timer.Stop();
					try
					{
						var method = recognizer.GetType().GetMethod("SendLongPress",
							BindingFlags.Instance | BindingFlags.NonPublic);
						var parent = FindParentView(recognizer);
						method?.Invoke(recognizer, new object?[] { parent });
					}
					catch { }

					// Also execute command if available
					var cmdProp = recognizer.GetType().GetProperty("Command");
					var paramProp = recognizer.GetType().GetProperty("CommandParameter");
					var cmd = cmdProp?.GetValue(recognizer) as System.Windows.Input.ICommand;
					var param = paramProp?.GetValue(recognizer);
					if (cmd?.CanExecute(param) == true)
						cmd.Execute(param);
				};
				timer.Start();
			};

			MouseButtonEventHandler upHandler = (s, e) =>
			{
				timer?.Stop();
				timer = null;
			};

			MouseEventHandler leaveHandler = (s, e) =>
			{
				timer?.Stop();
				timer = null;
			};

			view.MouseLeftButtonDown += downHandler;
			view.MouseLeftButtonUp += upHandler;
			view.MouseLeave += leaveHandler;
			EnsureHitTestable(view);

			GetCleanups(view).Add(new GestureCleanup(() =>
			{
				timer?.Stop();
				view.MouseLeftButtonDown -= downHandler;
				view.MouseLeftButtonUp -= upHandler;
				view.MouseLeave -= leaveHandler;
			}));
		}

		#endregion

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
