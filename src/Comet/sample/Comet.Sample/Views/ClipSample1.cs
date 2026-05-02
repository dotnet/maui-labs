using System;
using System.Collections.Generic;

using System.Text;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class ClipSample1 : Component
	{
				public override View Render() => VStack(
				Image("turtlerock.jpg")
				.Aspect(Aspect.AspectFill)
					.ClipShape(new Circle())
					.Border(new Circle().Stroke(Colors.White, lineWidth: 4))
					.Shadow(radius: 10)
			);

	}
}
