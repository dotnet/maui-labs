using System;
namespace Comet
{
	public class PanGesture : Gesture<PanGesture>
	{
		public PanGesture(Action<PanGesture> action) : base(action) { }

		/// <summary>Total horizontal displacement from start.</summary>
		public double TotalX { get; set; }

		/// <summary>Total vertical displacement from start.</summary>
		public double TotalY { get; set; }

		/// <summary>Horizontal velocity (points per second).</summary>
		public double VelocityX { get; set; }

		/// <summary>Vertical velocity (points per second).</summary>
		public double VelocityY { get; set; }

		/// <summary>Number of active touch points.</summary>
		public int TouchPoints { get; set; } = 1;

		public GestureStatus Status { get; set; }
	}
}
