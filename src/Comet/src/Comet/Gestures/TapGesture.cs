using System;
namespace Comet
{
	public class TapGesture : Gesture<TapGesture>
	{
		public TapGesture(Action<TapGesture> action) : base(action) { }

		/// <summary>Position X relative to the tapped view.</summary>
		public double X { get; set; }

		/// <summary>Position Y relative to the tapped view.</summary>
		public double Y { get; set; }

		/// <summary>Number of taps required to trigger (default 1).</summary>
		public int NumberOfTapsRequired { get; set; } = 1;

		/// <summary>Number of touches/fingers required (default 1).</summary>
		public int NumberOfTouchesRequired { get; set; } = 1;
	}
}
