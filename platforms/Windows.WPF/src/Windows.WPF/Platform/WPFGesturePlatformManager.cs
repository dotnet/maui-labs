#if !NET10_0
#nullable enable
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Graphics;
using MauiPoint = Microsoft.Maui.Graphics.Point;
using WpfPoint = System.Windows.Point;

namespace Microsoft.Maui.Platforms.Windows.WPF;

public sealed class WPFGesturePlatformManagerFactory : IGesturePlatformManagerFactory
{
	public IGesturePlatformManager CreateGesturePlatformManager(IViewHandler handler) =>
		new WPFGesturePlatformManager(handler);
}

sealed class WPFGesturePlatformManager : IGesturePlatformManager
{
	readonly FrameworkElement? _platformView;
	readonly View? _view;
	readonly List<Action> _detach = [];
	WpfPoint? _pressPoint;
	readonly List<(LongPressGestureRecognizer Recognizer, DispatcherTimer Timer, WpfPoint Start)> _longPresses = [];
	DataPackage? _dragData;
	int _panId;
	bool _isPanning;
	bool _dragStarted;
	bool _longPressCompleted;

	public WPFGesturePlatformManager(IViewHandler handler)
	{
		_platformView = handler.PlatformView as FrameworkElement;
		_view = handler.VirtualView as View;
		if (_platformView == null || _view == null)
			return;

		EnsureHitTestable(_platformView);
		AttachMouseHandlers();
		AttachDragDropHandlers();
	}

	void AttachMouseHandlers()
	{
		MouseButtonEventHandler down = OnMouseDown;
		MouseEventHandler move = OnMouseMove;
		MouseButtonEventHandler up = OnMouseUp;
		MouseEventHandler enter = OnMouseEnter;
		MouseEventHandler leave = OnMouseLeave;
		MouseWheelEventHandler wheel = OnMouseWheel;
		_platformView!.MouseLeftButtonDown += down;
		_platformView.MouseMove += move;
		_platformView.MouseLeftButtonUp += up;
		_platformView.MouseEnter += enter;
		_platformView.MouseLeave += leave;
		_platformView.MouseWheel += wheel;
		_detach.Add(() =>
		{
			_platformView.MouseLeftButtonDown -= down;
			_platformView.MouseMove -= move;
			_platformView.MouseLeftButtonUp -= up;
			_platformView.MouseEnter -= enter;
			_platformView.MouseLeave -= leave;
			_platformView.MouseWheel -= wheel;
		});
	}

	void AttachDragDropHandlers()
	{
		var originalAllowDrop = _platformView!.AllowDrop;
		_platformView!.AllowDrop = true;
		DragEventHandler over = OnDragOver;
		DragEventHandler leave = OnDragLeave;
		DragEventHandler drop = OnDrop;
		_platformView.DragOver += over;
		_platformView.DragLeave += leave;
		_platformView.Drop += drop;
		_detach.Add(() =>
		{
			_platformView.DragOver -= over;
			_platformView.DragLeave -= leave;
			_platformView.Drop -= drop;
			_platformView.AllowDrop = originalAllowDrop;
		});
	}

	void OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		_pressPoint = e.GetPosition(_platformView);
		_dragStarted = false;
		_isPanning = false;
		_longPressCompleted = false;
		_platformView!.CaptureMouse();
		var position = GetPosition(_pressPoint.Value);
		foreach (var pointer in Recognizers<PointerGestureRecognizer>())
			pointer.SendPointerPressed(_view!, position);

		foreach (var longPress in Recognizers<LongPressGestureRecognizer>())
		{
			var start = _pressPoint.Value;
			var timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(longPress.MinimumPressDuration),
			};
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				if (!_longPresses.Remove((longPress, timer, start)))
					return;
				var position = GetPosition(start);
				longPress.SendLongPressing(_view!, GestureStatus.Started, position);
				longPress.SendLongPressed(_view!, position);
				longPress.SendLongPressing(_view!, GestureStatus.Completed, position);
				_longPressCompleted = true;
			};
			_longPresses.Add((longPress, timer, start));
			timer.Start();
		}
	}

	void OnMouseMove(object sender, MouseEventArgs e)
	{
		var current = e.GetPosition(_platformView);
		var position = GetPosition(current);
		foreach (var pointer in Recognizers<PointerGestureRecognizer>())
			pointer.SendPointerMoved(_view!, position);

		foreach (var longPress in _longPresses.ToArray())
		{
			if (Distance(longPress.Start, current) <= longPress.Recognizer.AllowableMovement)
				continue;
			longPress.Timer.Stop();
			_longPresses.Remove(longPress);
		}

		if (_pressPoint is not WpfPoint start || e.LeftButton != MouseButtonState.Pressed)
			return;

		var totalX = current.X - start.X;
		var totalY = current.Y - start.Y;
		foreach (var pan in Recognizers<PanGestureRecognizer>())
		{
			var controller = (IPanGestureController)pan;
			if (!_isPanning)
				controller.SendPanStarted(_view!, _panId = Interlocked.Increment(ref _nextPanId));
			controller.SendPan(_view!, totalX, totalY, _panId);
		}
		_isPanning = Recognizers<PanGestureRecognizer>().Any();

		if (!_dragStarted && Distance(start, current) >= SystemParameters.MinimumHorizontalDragDistance)
			StartDrag(position);
	}

	void OnMouseUp(object sender, MouseButtonEventArgs e)
	{
		var current = e.GetPosition(_platformView);
		var position = GetPosition(current);
		foreach (var pointer in Recognizers<PointerGestureRecognizer>())
			pointer.SendPointerReleased(_view!, position);

		if (_pressPoint is WpfPoint start)
		{
			if (!_isPanning && !_dragStarted && !_longPressCompleted)
			{
				foreach (var tap in Recognizers<TapGestureRecognizer>()
					.Where(tap => e.ClickCount >= tap.NumberOfTapsRequired))
					tap.SendTapped(_view!, position);
			}

			if (_isPanning)
				foreach (var pan in Recognizers<PanGestureRecognizer>())
					((IPanGestureController)pan).SendPanCompleted(_view!, _panId);

			var totalX = current.X - start.X;
			var totalY = current.Y - start.Y;
			foreach (var swipe in Recognizers<SwipeGestureRecognizer>())
			{
				var controller = (ISwipeGestureController)swipe;
				controller.SendSwipe(_view!, totalX, totalY);
				controller.DetectSwipe(_view!, swipe.Direction);
			}
		}

		CancelLongPress();
		_pressPoint = null;
		_isPanning = false;
		_platformView!.ReleaseMouseCapture();
	}

	void OnMouseEnter(object sender, MouseEventArgs e)
	{
		var position = GetPosition(e.GetPosition(_platformView));
		foreach (var pointer in Recognizers<PointerGestureRecognizer>())
			pointer.SendPointerEntered(_view!, position);
	}

	void OnMouseLeave(object sender, MouseEventArgs e)
	{
		var position = GetPosition(e.GetPosition(_platformView));
		foreach (var pointer in Recognizers<PointerGestureRecognizer>())
			pointer.SendPointerExited(_view!, position);
		CancelLongPress();
		if (_isPanning)
			foreach (var pan in Recognizers<PanGestureRecognizer>())
				((IPanGestureController)pan).SendPanCanceled(_view!, _panId);
		_isPanning = false;
	}

	void OnMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if ((System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Control) == 0)
			return;

		var position = GetPosition(e.GetPosition(_platformView));
		foreach (var pinch in Recognizers<PinchGestureRecognizer>())
		{
			var controller = (IPinchGestureController)pinch;
			controller.SendPinchStarted(_view!, position(null) ?? MauiPoint.Zero);
			controller.SendPinch(_view!, e.Delta > 0 ? 1.1 : 0.9, position(null) ?? MauiPoint.Zero);
			controller.SendPinchEnded(_view!);
		}
		e.Handled = true;
	}

	void StartDrag(Func<IElement?, MauiPoint?> position)
	{
		var drag = Recognizers<DragGestureRecognizer>().FirstOrDefault(static recognizer => recognizer.CanDrag);
		if (drag == null)
			return;

		var args = drag.SendDragStarting(_view!, position);
#pragma warning disable CS0618
		if (args.Cancel || args.Handled)
			return;
#pragma warning restore CS0618
		_dragStarted = true;
		_dragData = args.Data;
		var dataObject = new DataObject(typeof(DataPackage), _dragData);
		var effect = DragDrop.DoDragDrop(_platformView!, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
		drag.SendDropCompleted(new DropCompletedEventArgs());
		if (effect == DragDropEffects.None)
			_dragData = null;
	}

	void OnDragOver(object sender, System.Windows.DragEventArgs e)
	{
		var package = GetDragData(e);
		var args = new Microsoft.Maui.Controls.DragEventArgs(package);
		foreach (var drop in Recognizers<DropGestureRecognizer>().Where(static recognizer => recognizer.AllowDrop))
			drop.SendDragOver(args);
		e.Effects = ToWpfEffect(args.AcceptedOperation);
		e.Handled = true;
	}

	void OnDragLeave(object sender, System.Windows.DragEventArgs e)
	{
		var args = new Microsoft.Maui.Controls.DragEventArgs(GetDragData(e));
		foreach (var drop in Recognizers<DropGestureRecognizer>())
			drop.SendDragLeave(args);
	}

	async void OnDrop(object sender, System.Windows.DragEventArgs e)
	{
		var args = new DropEventArgs(GetDragData(e).View);
		foreach (var drop in Recognizers<DropGestureRecognizer>().Where(static recognizer => recognizer.AllowDrop))
			await drop.SendDrop(args);
		e.Handled = true;
	}

	DataPackage GetDragData(System.Windows.DragEventArgs e) =>
		e.Data.GetData(typeof(DataPackage)) as DataPackage ?? _dragData ?? new DataPackage();

	IEnumerable<T> Recognizers<T>() where T : IGestureRecognizer =>
		_view!.GestureRecognizers.OfType<T>();

	Func<IElement?, MauiPoint?> GetPosition(WpfPoint point)
	{
		var screen = _platformView!.PointToScreen(point);
		return relativeTo =>
		{
			if (relativeTo?.Handler?.PlatformView is FrameworkElement relativePlatformView)
			{
				var translated = relativePlatformView.PointFromScreen(screen);
				return new MauiPoint(translated.X, translated.Y);
			}
			return new MauiPoint(point.X, point.Y);
		};
	}

	void CancelLongPress()
	{
		foreach (var longPress in _longPresses)
			longPress.Timer.Stop();
		_longPresses.Clear();
	}

	static double Distance(WpfPoint first, WpfPoint second) =>
		Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

	static DragDropEffects ToWpfEffect(DataPackageOperation operation) =>
		operation == DataPackageOperation.None ? DragDropEffects.None : DragDropEffects.Copy;

	static void EnsureHitTestable(FrameworkElement view)
	{
		if (view is System.Windows.Controls.Control control && control.Background == null)
			control.Background = System.Windows.Media.Brushes.Transparent;
		else if (view is System.Windows.Controls.Panel panel && panel.Background == null)
			panel.Background = System.Windows.Media.Brushes.Transparent;
	}

	static int _nextPanId;

	public void Dispose()
	{
		CancelLongPress();
		foreach (var detach in _detach)
			detach();
		_detach.Clear();
		_platformView?.ReleaseMouseCapture();
	}
}
#endif
