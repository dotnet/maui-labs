using System.Windows.Media;
using WProgressBar = System.Windows.Controls.ProgressBar;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ProgressBarHandler : WPFViewHandler<IProgress, WProgressBar>
	{
		protected override WProgressBar CreatePlatformView()
		{
			return new WProgressBar
			{
				Minimum = 0,
				Maximum = 1,
				MinHeight = 16,
				MinWidth = 100,
			};
		}

		public static void MapProgress(ProgressBarHandler handler, IProgress progress)
		{
			handler.PlatformView.Value = progress.Progress;
		}

		public static void MapProgressColor(ProgressBarHandler handler, IProgress progress)
		{
			var brush = ToBrush(progress.ProgressColor);
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
