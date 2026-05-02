using System;
using System.Windows;
using System.Windows.Media;
using WTextBox = System.Windows.Controls.TextBox;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class TimePickerHandler : WPFViewHandler<ITimePicker, WTextBox>
	{
		protected override WTextBox CreatePlatformView()
		{
			return new WTextBox
			{
				MinWidth = 80,
				IsReadOnly = false,
			};
		}

		protected override void ConnectHandler(WTextBox platformView)
		{
			base.ConnectHandler(platformView);
			platformView.LostFocus += OnLostFocus;
		}

		protected override void DisconnectHandler(WTextBox platformView)
		{
			platformView.LostFocus -= OnLostFocus;
			base.DisconnectHandler(platformView);
		}

		void OnLostFocus(object sender, System.Windows.RoutedEventArgs e)
		{
			if (VirtualView == null) return;
			if (TimeSpan.TryParse(PlatformView.Text, out var time))
				VirtualView.Time = time;
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapTime(TimePickerHandler handler, ITimePicker timePicker)
		{
			var time = timePicker.Time ?? TimeSpan.Zero;
			handler.PlatformView.Text = $"{time.Hours:D2}:{time.Minutes:D2}";
		}

		public static void MapFormat(TimePickerHandler handler, ITimePicker timePicker)
		{
			// WPF TextBox doesn't support time format directly; re-render time display.
			MapTime(handler, timePicker);
		}

		public static void MapTextColor(TimePickerHandler handler, ITimePicker timePicker)
		{
			var brush = ToBrush(timePicker.TextColor);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
		}

		public static void MapFont(TimePickerHandler handler, ITimePicker timePicker)
		{
			var font = timePicker.Font;

			if (font.Size > 0)
				handler.PlatformView.FontSize = font.Size;

			handler.PlatformView.FontWeight = font.Weight >= FontWeight.Bold
				? System.Windows.FontWeights.Bold
				: System.Windows.FontWeights.Normal;

			handler.PlatformView.FontStyle =
				(font.Slant == FontSlant.Italic || font.Slant == FontSlant.Oblique)
					? FontStyles.Italic
					: FontStyles.Normal;

			if (!string.IsNullOrEmpty(font.Family))
				handler.PlatformView.FontFamily = new FontFamily(font.Family);
		}

		public static void MapCharacterSpacing(TimePickerHandler handler, ITimePicker timePicker)
		{
			// WPF TextBox doesn't have a direct CharacterSpacing property.
		}
	}
}
