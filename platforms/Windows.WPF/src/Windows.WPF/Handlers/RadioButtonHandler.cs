using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Maui.Handlers;
using WRadioButton = System.Windows.Controls.RadioButton;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class RadioButtonHandler : WPFViewHandler<Microsoft.Maui.Controls.RadioButton, WRadioButton>
	{
		public static readonly PropertyMapper<Microsoft.Maui.Controls.RadioButton, RadioButtonHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.RadioButton.IsChecked)] = MapIsChecked,
				[nameof(Microsoft.Maui.Controls.RadioButton.Content)] = MapContent,
				[nameof(Microsoft.Maui.Controls.RadioButton.TextColor)] = MapTextColor,
				[nameof(Microsoft.Maui.Controls.RadioButton.FontSize)] = MapFontSize,
				[nameof(Microsoft.Maui.Controls.RadioButton.GroupName)] = MapGroupName,
			};

		public RadioButtonHandler() : base(Mapper) { }

		protected override WRadioButton CreatePlatformView()
		{
			var rb = new WRadioButton();
			rb.Checked += OnCheckedChanged;
			rb.Unchecked += OnCheckedChanged;
			return rb;
		}

		void OnCheckedChanged(object sender, RoutedEventArgs e)
		{
			if (VirtualView != null && PlatformView != null)
				VirtualView.IsChecked = PlatformView.IsChecked == true;
		}

		static void MapIsChecked(RadioButtonHandler handler, Microsoft.Maui.Controls.RadioButton view)
		{
			if (handler.PlatformView.IsChecked != view.IsChecked)
				handler.PlatformView.IsChecked = view.IsChecked;
		}

		static void MapContent(RadioButtonHandler handler, Microsoft.Maui.Controls.RadioButton view)
		{
			handler.PlatformView.Content = view.Content?.ToString() ?? "";
		}

		static void MapTextColor(RadioButtonHandler handler, Microsoft.Maui.Controls.RadioButton view)
		{
			if (view.TextColor != null)
			{
				var c = view.TextColor;
				handler.PlatformView.Foreground = new System.Windows.Media.SolidColorBrush(
					System.Windows.Media.Color.FromArgb((byte)(c.Alpha * 255), (byte)(c.Red * 255),
						(byte)(c.Green * 255), (byte)(c.Blue * 255)));
			}
		}

		static void MapFontSize(RadioButtonHandler handler, Microsoft.Maui.Controls.RadioButton view)
		{
			if (view.FontSize > 0)
				handler.PlatformView.FontSize = view.FontSize;
		}

		static void MapGroupName(RadioButtonHandler handler, Microsoft.Maui.Controls.RadioButton view)
		{
			if (!string.IsNullOrEmpty(view.GroupName))
				handler.PlatformView.GroupName = view.GroupName;
		}

		protected override void DisconnectHandler(WRadioButton platformView)
		{
			platformView.Checked -= OnCheckedChanged;
			platformView.Unchecked -= OnCheckedChanged;
			base.DisconnectHandler(platformView);
		}
	}
}
