using System;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Question1d : Component
	{
				public override View Render()
		{
			return ScrollView(
					VStack(
						Image("turtlerock.jpg").Frame(75, 75).Padding(4),
						Text("Title").FitHorizontal(),
						Text("Description").FitHorizontal().FontSize(12).Color(Colors.Grey)
					).FillHorizontal()

			);
		}
	}
}
