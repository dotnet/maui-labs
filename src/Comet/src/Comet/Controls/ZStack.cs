using Comet.Layout;
using Microsoft.Maui.Layouts;

namespace Comet
{
	public partial class ZStack : AbstractLayout
	{
		protected override ILayoutManager CreateLayoutManager() => new ZStackLayoutManager(this);
		protected override Thickness GetDefaultPadding() => Thickness.Zero;
	}
}
