using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class NestedViews : Component
	{
		public override View Render() => new View
		{
			Body = () => new View
			{
				Body = () => Text("Hi!")
			}
		};
	}
}