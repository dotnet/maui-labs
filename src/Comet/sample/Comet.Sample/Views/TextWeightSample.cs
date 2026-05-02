using System;
using Microsoft.Maui;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class TextWeightSample : Component
	{
				public override View Render() => ScrollView(
			VStack(
				Text($"Black {(int)FontWeight.Black}").FontWeight (FontWeight.Black),
				Text($"Heavy {(int)FontWeight.Heavy}").FontWeight (FontWeight.Heavy),
				Text($"Bold {(int)FontWeight.Bold}").FontWeight (FontWeight.Bold),
				Text($"Semibold {(int)FontWeight.Semibold}").FontWeight (FontWeight.Semibold),
				Text($"Medium {(int)FontWeight.Medium}").FontWeight (FontWeight.Medium),
				Text($"Regular {(int)FontWeight.Regular}").FontWeight (FontWeight.Regular),
				Text($"Light {(int)FontWeight.Ultralight}").FontWeight (FontWeight.Light),
				Text($"Ultralight {(int)FontWeight.Ultralight}").FontWeight (FontWeight.Ultralight),
				Text($"Thin {(int)FontWeight.Thin}").FontWeight (FontWeight.Thin),
				Text($"Black Oblique {(int)FontWeight.Black}").FontWeight (FontWeight.Black).FontSlant (FontSlant.Oblique),
				Text($"Heavy Oblique {(int)FontWeight.Heavy}").FontWeight (FontWeight.Heavy).FontSlant (FontSlant.Oblique),
				Text($"Bold Italic {(int)FontWeight.Bold}").FontWeight (FontWeight.Bold).FontSlant (FontSlant.Italic),
				Text($"Semibold Oblique {(int)FontWeight.Semibold}").FontWeight (FontWeight.Semibold).FontSlant (FontSlant.Oblique),
				Text($"Medium Oblique {(int)FontWeight.Medium}").FontWeight (FontWeight.Medium).FontSlant (FontSlant.Oblique),
				Text($"Regular Italic {(int)FontWeight.Regular}").FontWeight (FontWeight.Regular).FontSlant (FontSlant.Italic),
				Text($"Light Oblique {(int)FontWeight.Ultralight}").FontWeight (FontWeight.Light).FontSlant (FontSlant.Oblique),
				Text($"Ultralight Oblique {(int)FontWeight.Ultralight}").FontWeight (FontWeight.Ultralight).FontSlant (FontSlant.Oblique),
				Text($"Thin Oblique {(int)FontWeight.Thin}").FontWeight (FontWeight.Thin).FontSlant (FontSlant.Oblique)
			)
		);
	}
}
