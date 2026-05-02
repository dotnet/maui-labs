using System;
namespace Comet
{
	public class LongPressGesture : Gesture<LongPressGesture>
	{
		public LongPressGesture(Action<LongPressGesture> action) : base(action) { }
		public double MinimumPressDuration { get; set; } = 0.5;
	}
}
