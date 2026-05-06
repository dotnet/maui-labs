using WSlider = System.Windows.Controls.Slider;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SliderHandler : WPFViewHandler<ISlider, WSlider>
	{
		protected override WSlider CreatePlatformView()
		{
			return new WSlider
			{
				Minimum = 0,
				Maximum = 1,
				MinWidth = 100,
			};
		}

		protected override void ConnectHandler(WSlider platformView)
		{
			base.ConnectHandler(platformView);
			platformView.ValueChanged += OnValueChanged;
		}

		protected override void DisconnectHandler(WSlider platformView)
		{
			platformView.ValueChanged -= OnValueChanged;
			base.DisconnectHandler(platformView);
		}

		void OnValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			if (VirtualView == null) return;
			VirtualView.Value = e.NewValue;
		}

		public static void MapValue(SliderHandler handler, ISlider slider)
		{
			if (handler.PlatformView.Value != slider.Value)
				handler.PlatformView.Value = slider.Value;
		}

		public static void MapMinimum(SliderHandler handler, ISlider slider)
		{
			handler.PlatformView.Minimum = slider.Minimum;
		}

		public static void MapMaximum(SliderHandler handler, ISlider slider)
		{
			handler.PlatformView.Maximum = slider.Maximum;
		}

		public static void MapMinimumTrackColor(SliderHandler handler, ISlider slider)
		{
			if (slider.MinimumTrackColor != null)
				handler.PlatformView.Foreground = ToBrush(slider.MinimumTrackColor);
		}

		public static void MapMaximumTrackColor(SliderHandler handler, ISlider slider)
		{
			if (slider.MaximumTrackColor != null)
				handler.PlatformView.Background = ToBrush(slider.MaximumTrackColor);
		}

		public static void MapThumbColor(SliderHandler handler, ISlider slider)
		{
			// WPF Slider does not expose the thumb brush directly
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
