using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class VirtualListViewSample : Component
	{
		public override View Render() => new ListView<int>
		{
			Count = () => 10,
			ItemFor = (i) => i,
			ViewFor = (i) => Text(i.ToString()),
		};
	}
}
