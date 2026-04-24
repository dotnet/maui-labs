using static Comet.CometControls;

﻿namespace Comet.Samples;

public class VGridSample : Component
{
		public override View Render() => new VGrid(4)
	{
		Enumerable.Range(0,20).Select(x=>
			Text($"{x}")
				.HorizontalTextAlignment(TextAlignment.Center)
		),
	};
}