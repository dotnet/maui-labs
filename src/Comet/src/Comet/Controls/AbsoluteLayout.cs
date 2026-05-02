using Comet.Layout;
using Microsoft.Maui.Layouts;

namespace Comet
{
	public class AbsoluteLayout : AbstractLayout
	{
		protected override ILayoutManager CreateLayoutManager() => new Layout.AbsoluteLayoutManager(this);
	}

	[Flags]
	public enum AbsoluteLayoutFlags
	{
		None = 0,
		XProportional = 1,
		YProportional = 2,
		WidthProportional = 4,
		HeightProportional = 8,
		PositionProportional = XProportional | YProportional,
		SizeProportional = WidthProportional | HeightProportional,
		All = PositionProportional | SizeProportional
	}
}
