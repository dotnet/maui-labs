using System;
using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// Pointer gesture for hover/mouse interactions (desktop platforms).
	/// Enhanced with position data for each pointer event.
	/// Supports both MVU callback pattern and MVVM command pattern.
	/// </summary>
	public class PointerGesture : Gesture
	{
		/// <summary>
		/// MVU-style callback invoked when pointer enters the view.
		/// </summary>
		public Action<View, Point> PointerEntered { get; set; }

		/// <summary>
		/// MVU-style callback invoked when pointer moves within the view.
		/// </summary>
		public Action<View, Point> PointerMoved { get; set; }

		/// <summary>
		/// MVU-style callback invoked when pointer exits the view.
		/// </summary>
		public Action<View, Point> PointerExited { get; set; }

		/// <summary>
		/// MVU-style callback invoked when pointer button is pressed.
		/// </summary>
		public Action<View, Point> PointerPressed { get; set; }

		/// <summary>
		/// MVU-style callback invoked when pointer button is released.
		/// </summary>
		public Action<View, Point> PointerReleased { get; set; }

		/// <summary>
		/// MVVM-style command invoked when pointer enters the view.
		/// </summary>
		public ICommand PointerEnteredCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when pointer moves within the view.
		/// </summary>
		public ICommand PointerMovedCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when pointer exits the view.
		/// </summary>
		public ICommand PointerExitedCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when pointer button is pressed.
		/// </summary>
		public ICommand PointerPressedCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when pointer button is released.
		/// </summary>
		public ICommand PointerReleasedCommand { get; set; }

		/// <summary>
		/// Mouse button(s) to track. 
		/// Values: 0=Primary (left), 1=Secondary (right), 2=Middle, etc.
		/// Use -1 to track all buttons (default).
		/// </summary>
		public int ButtonsMask { get; set; } = -1; // All buttons by default

		/// <summary>
		/// Backward-compatible setter for PointerEntered (no position).
		/// </summary>
		public void SetPointerEntered(Action<View> action) => PointerEntered = (v, _) => action(v);

		/// <summary>
		/// Backward-compatible setter for PointerExited (no position).
		/// </summary>
		public void SetPointerExited(Action<View> action) => PointerExited = (v, _) => action(v);

		/// <summary>
		/// Backward-compatible setter for PointerMoved (no position).
		/// </summary>
		public void SetPointerMoved(Action<View> action) => PointerMoved = (v, _) => action(v);

		/// <summary>
		/// Backward-compatible setter for PointerPressed (no position).
		/// </summary>
		public void SetPointerPressed(Action<View> action) => PointerPressed = (v, _) => action(v);

		/// <summary>
		/// Backward-compatible setter for PointerReleased (no position).
		/// </summary>
		public void SetPointerReleased(Action<View> action) => PointerReleased = (v, _) => action(v);
	}
}

