using System.Windows;
using System.Windows.Media;
using WDatePicker = System.Windows.Controls.DatePicker;
using WSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class DatePickerHandler : WPFViewHandler<IDatePicker, WDatePicker>
	{
		protected override WDatePicker CreatePlatformView()
		{
			return new WDatePicker
			{
				MinWidth = 100,
			};
		}

		protected override void ConnectHandler(WDatePicker platformView)
		{
			base.ConnectHandler(platformView);
			platformView.SelectedDateChanged += OnSelectedDateChanged;
		}

		protected override void DisconnectHandler(WDatePicker platformView)
		{
			platformView.SelectedDateChanged -= OnSelectedDateChanged;
			base.DisconnectHandler(platformView);
		}

		void OnSelectedDateChanged(object? sender, WSelectionChangedEventArgs e)
		{
			if (VirtualView == null || PlatformView.SelectedDate == null) return;
			VirtualView.Date = PlatformView.SelectedDate.Value;
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapDate(DatePickerHandler handler, IDatePicker datePicker)
		{
			handler.PlatformView.SelectedDate = datePicker.Date;
		}

		public static void MapMinimumDate(DatePickerHandler handler, IDatePicker datePicker)
		{
			handler.PlatformView.DisplayDateStart = datePicker.MinimumDate;
		}

		public static void MapMaximumDate(DatePickerHandler handler, IDatePicker datePicker)
		{
			handler.PlatformView.DisplayDateEnd = datePicker.MaximumDate;
		}

		public static void MapFormat(DatePickerHandler handler, IDatePicker datePicker)
		{
			// WPF DatePicker doesn't expose a format property directly.
		}

		public static void MapTextColor(DatePickerHandler handler, IDatePicker datePicker)
		{
			var brush = ToBrush(datePicker.TextColor);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
		}

		public static void MapFont(DatePickerHandler handler, IDatePicker datePicker)
		{
			var font = datePicker.Font;

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

		public static void MapCharacterSpacing(DatePickerHandler handler, IDatePicker datePicker)
		{
			// WPF DatePicker doesn't have a direct CharacterSpacing property.
		}
	}
}
