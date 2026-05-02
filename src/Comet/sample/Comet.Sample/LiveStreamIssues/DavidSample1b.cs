

using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples.LiveStreamIssues
{
	public class DavidSample1b : Component
	{
				public override View Render() =>
			HStack(LayoutAlignment.Center,
				Spacer(),
				new ShapeView(new Circle().Stroke(Colors.Black, 2f)).Frame(44,44),
				Spacer()
			);
	}
}
