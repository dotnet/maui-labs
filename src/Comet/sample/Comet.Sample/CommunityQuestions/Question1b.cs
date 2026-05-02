using System;

using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Question1b : Component
	{
				public override View Render() =>
			VStack(
				Image("turtlerock.jpg")
					.Frame(75, 75)
					.Padding(4),
				Text("Title")
					.HorizontalTextAlignment(TextAlignment.Center),
				Text("Description")
					.HorizontalTextAlignment(TextAlignment.Center)
					.FontSize(12)
					.Color(Colors.Grey)
			).FillHorizontal();
	}
}