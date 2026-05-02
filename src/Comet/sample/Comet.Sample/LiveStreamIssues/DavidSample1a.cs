

using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples.LiveStreamIssues
{
	public class DavidSample1a : Component
	{
				public override View Render() =>
			VStack(LayoutAlignment.Center,
				new ShapeView(new Circle().Stroke(Colors.Black, 2f)).Frame(44,44)
			);
	}
}
