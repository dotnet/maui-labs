using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class TabViewSample : Component
	{

				public override View Render() => TabView(
			HStack(
				Text("Tab 1")
			).TabText("Tab 1"),
			HStack(
				Text("Tab 2")
			).TabText("Tab 2")
		);
	}
}
