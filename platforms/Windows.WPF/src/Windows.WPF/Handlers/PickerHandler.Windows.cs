using System.Windows;
using System.Windows.Media;
using WComboBox = System.Windows.Controls.ComboBox;
using WSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class PickerHandler : WPFViewHandler<IPicker, WComboBox>
	{
		bool _updating;

		protected override WComboBox CreatePlatformView()
		{
			return new WComboBox
			{
				MinWidth = 100,
			};
		}

		protected override void ConnectHandler(WComboBox platformView)
		{
			base.ConnectHandler(platformView);
			platformView.SelectionChanged += OnSelectionChanged;
		}

		protected override void DisconnectHandler(WComboBox platformView)
		{
			platformView.SelectionChanged -= OnSelectionChanged;
			base.DisconnectHandler(platformView);
		}

		void OnSelectionChanged(object sender, WSelectionChangedEventArgs e)
		{
			if (VirtualView == null || _updating) return;
			_updating = true;
			try
			{
				VirtualView.SelectedIndex = PlatformView.SelectedIndex;
			}
			finally
			{
				_updating = false;
			}
		}

		static System.Windows.Media.SolidColorBrush? ToBrush(Microsoft.Maui.Graphics.Color? color)
		{
			if (color == null) return null;
			return new System.Windows.Media.SolidColorBrush(
				System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255)));
		}

		public static void MapTitle(PickerHandler handler, IPicker picker)
		{
			if (picker.SelectedIndex < 0 && !string.IsNullOrEmpty(picker.Title))
				handler.PlatformView.Text = picker.Title;
		}

		public static void MapTitleColor(PickerHandler handler, IPicker picker)
		{
			if (picker.TitleColor != null && picker.SelectedIndex < 0)
			{
				var brush = ToBrush(picker.TitleColor);
				if (brush != null)
					handler.PlatformView.Foreground = brush;
			}
		}

		public static void MapSelectedIndex(PickerHandler handler, IPicker picker)
		{
			if (handler._updating) return;
			handler._updating = true;
			try
			{
				if (handler.PlatformView.SelectedIndex != picker.SelectedIndex)
					handler.PlatformView.SelectedIndex = picker.SelectedIndex;
			}
			finally
			{
				handler._updating = false;
			}
		}

		public static void MapTextColor(PickerHandler handler, IPicker picker)
		{
			var brush = ToBrush(picker.TextColor);
			if (brush != null)
				handler.PlatformView.Foreground = brush;
		}

		public static void MapFont(PickerHandler handler, IPicker picker)
		{
			var font = picker.Font;

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

		public static void MapCharacterSpacing(PickerHandler handler, IPicker picker)
		{
			// WPF ComboBox doesn't have a direct CharacterSpacing property.
		}

		public static void MapBackground(PickerHandler handler, IPicker picker)
		{
			if (picker.Background is SolidPaint solidPaint)
			{
				var brush = ToBrush(solidPaint.Color);
				if (brush != null)
					handler.PlatformView.Background = brush;
			}
		}

		public static void MapHorizontalTextAlignment(PickerHandler handler, IPicker picker)
		{
			handler.PlatformView.HorizontalContentAlignment = picker.HorizontalTextAlignment switch
			{
				TextAlignment.Center => System.Windows.HorizontalAlignment.Center,
				TextAlignment.End => System.Windows.HorizontalAlignment.Right,
				_ => System.Windows.HorizontalAlignment.Left,
			};
		}

		public static void MapItems(PickerHandler handler, IPicker picker)
		{
			handler._updating = true;
			try
			{
				handler.PlatformView.Items.Clear();
				var count = picker.GetCount();
				for (int i = 0; i < count; i++)
				{
					handler.PlatformView.Items.Add(picker.GetItem(i));
				}
			}
			finally
			{
				handler._updating = false;
			}
		}
	}
}
