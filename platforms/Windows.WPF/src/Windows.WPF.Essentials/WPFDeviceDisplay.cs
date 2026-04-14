using Microsoft.Maui.Devices;

#pragma warning disable CS0067 // Event is never used (required by IDeviceDisplay interface)

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFDeviceDisplay : IDeviceDisplay
	{
		public bool KeepScreenOn { get; set; }

		public DisplayInfo MainDisplayInfo
		{
			get
			{
				var screen = System.Windows.SystemParameters.PrimaryScreenWidth;
				var height = System.Windows.SystemParameters.PrimaryScreenHeight;
				var dpi = GetDpi();
				var density = dpi / 96.0;
				return new DisplayInfo(
					width: screen * density,
					height: height * density,
					density: density,
					orientation: screen > height ? DisplayOrientation.Landscape : DisplayOrientation.Portrait,
					rotation: DisplayRotation.Rotation0,
					rate: 60);
			}
		}

		public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged;

		static double GetDpi()
		{
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters());
			var dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
			return dpiX;
		}
	}
}
