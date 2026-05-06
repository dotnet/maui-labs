

using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples.LiveStreamIssues
{
	public class DavidSample2 : Component
	{
				public override View Render() =>
			VStack(
				HStack(
					new ShapeView(new Circle().Stroke(Colors.Black, 2f)).Frame(44,44)
				)
			).Alignment(Alignment.BottomTrailing);
	}
}
