using System;
namespace Comet
{
	public class SwipeGesture : Gesture<SwipeGesture>
	{
		public SwipeGesture(Action<SwipeGesture> action) : base(action) { }

		public SwipeDirection Direction { get; set; }

		/// <summary>Swipe velocity in points per second.</summary>
		public double Velocity { get; set; }

		/// <summary>Horizontal offset of the swipe.</summary>
		public double OffsetX { get; set; }

		/// <summary>Vertical offset of the swipe.</summary>
		public double OffsetY { get; set; }

		/// <summary>Minimum swipe distance threshold in device-independent pixels.</summary>
		public double Threshold { get; set; } = 100;
	}

	public enum SwipeDirection
	{
		Left,
		Right,
		Up,
		Down
	}
}
