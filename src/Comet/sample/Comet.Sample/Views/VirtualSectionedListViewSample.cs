using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class VirtualSectionedListViewSample : Component
	{
		public override View Render() => new SectionedListView<int>
		{
			SectionCount = () => 10,
			SectionFor = (s) => new Section<int>
			{
				Header = Text($"Header: {s}"),
				Count = () => 10,
				ItemFor = (index) => index,
				ViewFor = (i) => Text($"Row: {i}"),
			},
		};
	}
}
