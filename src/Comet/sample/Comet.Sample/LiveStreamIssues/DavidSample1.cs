

using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples.LiveStreamIssues
{
	public class DavidSample1 : Component
	{
				public override View Render() =>
			VStack(LayoutAlignment.Center,
				HStack(
					new ShapeView(new Circle().Stroke(Colors.Black, 2f)).Frame(44,44)
				)
			).Alignment(Alignment.Top);
	}
}
