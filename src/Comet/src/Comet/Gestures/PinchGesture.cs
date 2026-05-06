using System;
namespace Comet
{
	public class PinchGesture : Gesture<PinchGesture>
	{
		public PinchGesture(Action<PinchGesture> action) : base(action) { }

		/// <summary>Current scale factor (1.0 = no scale).</summary>
		public double Scale { get; set; } = 1.0;

		/// <summary>Scale velocity (rate of change).</summary>
		public double ScaleVelocity { get; set; }

		/// <summary>Origin X of the pinch center point relative to the view.</summary>
		public double OriginX { get; set; }

		/// <summary>Origin Y of the pinch center point relative to the view.</summary>
		public double OriginY { get; set; }

		public GestureStatus Status { get; set; }
	}
}
