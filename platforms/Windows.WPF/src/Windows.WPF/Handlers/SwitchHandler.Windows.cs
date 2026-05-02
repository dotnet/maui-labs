using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using WBorder = System.Windows.Controls.Border;
using WTrigger = System.Windows.Trigger;
using WSetter = System.Windows.Setter;
using WColors = System.Windows.Media.Colors;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SwitchHandler : WPFViewHandler<ISwitch, ToggleButton>
	{
		static readonly System.Windows.Media.SolidColorBrush DefaultTrackOff = new(System.Windows.Media.Color.FromRgb(200, 200, 200));
		static readonly System.Windows.Media.SolidColorBrush DefaultTrackOn = new(System.Windows.Media.Color.FromRgb(76, 175, 80));
		static readonly System.Windows.Media.SolidColorBrush ThumbBrush = new(WColors.White);

		protected override ToggleButton CreatePlatformView()
		{
			var toggle = new ToggleButton
			{
				Width = 50,
				Height = 26,
				Padding = new System.Windows.Thickness(0),
				Template = CreateSwitchTemplate(),
			};
			return toggle;
		}

		static System.Windows.Controls.ControlTemplate CreateSwitchTemplate()
		{
			var template = new System.Windows.Controls.ControlTemplate(typeof(ToggleButton));
			var borderFactory = new FrameworkElementFactory(typeof(WBorder));
			borderFactory.SetValue(WBorder.CornerRadiusProperty, new global::System.Windows.CornerRadius(13));
			borderFactory.SetValue(WBorder.BackgroundProperty, DefaultTrackOff);
			borderFactory.Name = "TrackBorder";

			var canvas = new FrameworkElementFactory(typeof(Canvas));
			canvas.SetValue(Canvas.HeightProperty, 22.0);
			canvas.SetValue(Canvas.WidthProperty, 46.0);
			canvas.SetValue(FrameworkElement.MarginProperty, new System.Windows.Thickness(2));

			var thumb = new FrameworkElementFactory(typeof(Ellipse));
			thumb.SetValue(Ellipse.WidthProperty, 22.0);
			thumb.SetValue(Ellipse.HeightProperty, 22.0);
			thumb.SetValue(Ellipse.FillProperty, ThumbBrush);
			thumb.SetValue(Canvas.LeftProperty, 0.0);
			thumb.Name = "Thumb";

			canvas.AppendChild(thumb);
			borderFactory.AppendChild(canvas);
			template.VisualTree = borderFactory;

			// Checked trigger - move thumb right and change color
			var checkedTrigger = new WTrigger { Property = ToggleButton.IsCheckedProperty, Value = true };
			checkedTrigger.Setters.Add(new WSetter(Canvas.LeftProperty, 24.0, "Thumb"));
			checkedTrigger.Setters.Add(new WSetter(WBorder.BackgroundProperty, DefaultTrackOn, "TrackBorder"));
			template.Triggers.Add(checkedTrigger);

			return template;
		}

		protected override void ConnectHandler(ToggleButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Checked += OnToggled;
			platformView.Unchecked += OnToggled;
		}

		protected override void DisconnectHandler(ToggleButton platformView)
		{
			platformView.Checked -= OnToggled;
			platformView.Unchecked -= OnToggled;
			base.DisconnectHandler(platformView);
		}

		void OnToggled(object sender, RoutedEventArgs e)
		{
			if (VirtualView == null) return;
			VirtualView.IsOn = PlatformView.IsChecked ?? false;
		}

		public static void MapIsOn(SwitchHandler handler, ISwitch @switch)
		{
			if (handler.PlatformView.IsChecked != @switch.IsOn)
				handler.PlatformView.IsChecked = @switch.IsOn;
		}

		public static void MapTrackColor(SwitchHandler handler, ISwitch @switch)
		{
			// TrackColor will be applied via the ControlTemplate triggers
		}

		public static void MapThumbColor(SwitchHandler handler, ISwitch @switch)
		{
			// ThumbColor would need template modification
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
