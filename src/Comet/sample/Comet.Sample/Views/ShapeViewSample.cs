using static Comet.CometControls;

﻿namespace Comet.Samples;
public class ShapeViewSample : Component
{
		public override View Render() => ZStack(
			new ShapeView(
				new Circle()
					.Stroke(Colors.Blue, 4)
					.Fill(Colors.Black)
			)
				.Frame(width:80,height:80),
			new ShapeView(
				new Rectangle()
					.Fill(Colors.Red)
			).Frame(width:40,height:40)
	);
}
