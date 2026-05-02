using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class ContinuosSample : Component
	{
		readonly Reactive<string> _strokeColor = "#000000";

				public override View Render()
		{

			return Grid(
				columns: new object[] { "*", "*" },
				rows: null,
				TextField(_strokeColor, "Enter code here").Cell(row:0, column: 0),
				Button("Controls appear here").Cell(row:0, column:1)
			);
		}
	}
}
