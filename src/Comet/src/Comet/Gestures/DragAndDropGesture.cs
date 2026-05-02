using System;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Comet
{
	/// <summary>
	/// Drag gesture recognizer for drag source support.
	/// Supports both MVU callback pattern and MVVM command pattern.
	/// </summary>
	public class DragGesture : Gesture
	{
		/// <summary>
		/// MVU-style callback invoked when drag starts.
		/// Returns data object to be transferred in drag operation.
		/// </summary>
		public Func<View, object> DragStarting { get; set; }

		/// <summary>
		/// MVU-style callback invoked when drag is completed (dropped).
		/// </summary>
		public Action<View> DropCompleted { get; set; }

		/// <summary>
		/// MVVM-style command invoked when drag starts.
		/// Alternative to DragStarting callback.
		/// </summary>
		public ICommand DragStartingCommand { get; set; }

		/// <summary>
		/// Parameter to pass to DragStartingCommand.
		/// </summary>
		public object DragStartingCommandParameter { get; set; }

		/// <summary>
		/// MVVM-style command invoked when drag is completed (dropped).
		/// Alternative to DropCompleted callback.
		/// </summary>
		public ICommand DropCompletedCommand { get; set; }

		/// <summary>
		/// Parameter to pass to DropCompletedCommand.
		/// </summary>
		public object DropCompletedCommandParameter { get; set; }

		/// <summary>
		/// Gets or sets whether the view can be dragged.
		/// Default is true.
		/// </summary>
		public bool CanDrag { get; set; } = true;
	}

	/// <summary>
	/// Drop gesture recognizer for drop target support.
	/// Supports both MVU callback pattern and MVVM command pattern.
	/// </summary>
	public class DropGesture : Gesture
	{
		/// <summary>
		/// MVU-style callback invoked when a dragged element enters the drop target.
		/// Returns true if drop is accepted, false to reject.
		/// </summary>
		public Func<View, object, bool> DragOver { get; set; }

		/// <summary>
		/// MVU-style callback invoked when a dragged element is dropped on this target.
		/// </summary>
		public Action<View, object> Drop { get; set; }

		/// <summary>
		/// MVU-style callback invoked when a dragged element leaves the drop target.
		/// </summary>
		public Action<View> DragLeave { get; set; }

		/// <summary>
		/// MVVM-style command invoked when a dragged element is over the drop target.
		/// Alternative to DragOver callback.
		/// </summary>
		public ICommand DragOverCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when a dragged element leaves the drop target.
		/// Alternative to DragLeave callback.
		/// </summary>
		public ICommand DragLeaveCommand { get; set; }

		/// <summary>
		/// MVVM-style command invoked when a dragged element is dropped.
		/// Alternative to Drop callback.
		/// </summary>
		public ICommand DropCommand { get; set; }

		/// <summary>
		/// Parameter to pass to DropCommand.
		/// </summary>
		public object DropCommandParameter { get; set; }

		/// <summary>
		/// Gets or sets whether the view accepts drops.
		/// Default is true.
		/// </summary>
		public bool AllowDrop { get; set; } = true;
	}
}


