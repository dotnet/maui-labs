using System.Windows;
using WCheckBox = System.Windows.Controls.CheckBox;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class CheckBoxHandler : WPFViewHandler<ICheckBox, WCheckBox>
	{
		protected override WCheckBox CreatePlatformView() => new WCheckBox();

		protected override void ConnectHandler(WCheckBox platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Checked += OnToggled;
			platformView.Unchecked += OnToggled;
		}

		protected override void DisconnectHandler(WCheckBox platformView)
		{
			platformView.Checked -= OnToggled;
			platformView.Unchecked -= OnToggled;
			base.DisconnectHandler(platformView);
		}

		void OnToggled(object sender, RoutedEventArgs e)
		{
			if (VirtualView == null) return;
			VirtualView.IsChecked = PlatformView.IsChecked ?? false;
		}

		public static void MapIsChecked(CheckBoxHandler handler, ICheckBox checkBox)
		{
			if (handler.PlatformView.IsChecked != checkBox.IsChecked)
				handler.PlatformView.IsChecked = checkBox.IsChecked;
		}

		public static void MapForeground(CheckBoxHandler handler, ICheckBox checkBox)
		{
			if (checkBox.Foreground is Microsoft.Maui.Graphics.SolidPaint sp && sp.Color != null)
				handler.PlatformView.Foreground = ToBrush(sp.Color);
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
