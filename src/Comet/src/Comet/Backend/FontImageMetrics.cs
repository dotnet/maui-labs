#nullable enable
namespace Comet.Backend
{
	internal readonly struct FontImageScaleMetrics
	{
		public FontImageScaleMetrics(float deviceDensity, float scaledDensity)
		{
			DeviceDensity = deviceDensity > 0 ? deviceDensity : 1f;
			ScaledDensity = scaledDensity > 0 ? scaledDensity : DeviceDensity;
		}

		public float DeviceDensity { get; }
		public float ScaledDensity { get; }

		public float RasterDensity(bool autoScaling) =>
			autoScaling ? ScaledDensity : DeviceDensity;

		public double PixelsToLogical(double pixels) =>
			pixels / DeviceDensity;
	}

	internal enum NativeFontWeightClass
	{
		Thin,
		UltraLight,
		Light,
		Regular,
		Medium,
		Semibold,
		Bold,
		Heavy,
		Black,
	}

	internal static class NativeFontWeightMapping
	{
		public static NativeFontWeightClass Classify(int weight) => weight switch
		{
			>= 1 and < 200 => NativeFontWeightClass.Thin,
			>= 200 and < 300 => NativeFontWeightClass.UltraLight,
			>= 300 and < 400 => NativeFontWeightClass.Light,
			>= 400 and < 500 => NativeFontWeightClass.Regular,
			>= 500 and < 600 => NativeFontWeightClass.Medium,
			>= 600 and < 700 => NativeFontWeightClass.Semibold,
			>= 700 and < 800 => NativeFontWeightClass.Bold,
			>= 800 and < 900 => NativeFontWeightClass.Heavy,
			>= 900 => NativeFontWeightClass.Black,
			_ => NativeFontWeightClass.Regular,
		};
	}
}
