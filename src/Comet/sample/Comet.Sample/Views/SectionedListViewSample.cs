using System;
using System.Linq;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class SectionedListViewSample : Component
	{
		public override View Render()
		{
			int total = 10;
			var sections = Enumerable.Range(0, total).Select(s => new Section(header: Text(s.ToString()))
			{
				Enumerable.Range(0, total).Select(r => Text(r.ToString())),
			}).ToList();
			return new SectionedListView(sections);
		}
	}
}
