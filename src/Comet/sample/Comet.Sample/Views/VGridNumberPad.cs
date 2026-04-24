using static Comet.CometControls;

﻿namespace Comet.Samples;

public class VGridNumberPad : Component
{
		public override View Render() => new VGrid(3)
	{
		Button("7"),Button("8"),Button("9"),
		Button("4"),Button("5"),Button("6"),
		Button("1"),Button("2"),Button("3"),
		Button("0").NextColumn()
	};
}
