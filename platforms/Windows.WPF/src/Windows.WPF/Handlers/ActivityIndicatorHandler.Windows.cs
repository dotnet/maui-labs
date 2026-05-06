using System.Windows;
using System.Windows.Controls;
using WProgressBar = System.Windows.Controls.ProgressBar;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ActivityIndicatorHandler : WPFViewHandler<IActivityIndicator, WProgressBar>
	{
		protected override WProgressBar CreatePlatformView()
		{
			return new WProgressBar
			{
				IsIndeterminate = true,
				MinHeight = 20,
				MinWidth = 100,
			};
		}

		public static void MapIsRunning(ActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
			handler.PlatformView.IsIndeterminate = activityIndicator.IsRunning;
			handler.PlatformView.Visibility = activityIndicator.IsRunning ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		}

		public static void MapColor(ActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
			var brush = ToBrush(activityIndicator.Color);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}
	}
}
