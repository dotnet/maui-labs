using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometControlsGallery
{
	public class SectionCard : ViewModifier
	{
		public override View Apply(View view)
		{
			view
				.Background(new SolidPaint(ColorTokens.Surface.Resolve(ThemeManager.Current())))
				.ClipShape(new RoundedRectangle(8))
				.Padding(new Thickness(20));
			return view;
		}
	}
}
