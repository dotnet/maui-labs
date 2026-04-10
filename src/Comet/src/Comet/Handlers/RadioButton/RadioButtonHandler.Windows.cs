using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler : ViewHandler<RadioButton, Microsoft.UI.Xaml.Controls.RadioButton>
	{
		protected override Microsoft.UI.Xaml.Controls.RadioButton CreatePlatformView()
		{
			return new Microsoft.UI.Xaml.Controls.RadioButton();
		}

		protected override void ConnectHandler(Microsoft.UI.Xaml.Controls.RadioButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Checked += OnPlatformChecked;
			platformView.Unchecked += OnPlatformUnchecked;
		}

		protected override void DisconnectHandler(Microsoft.UI.Xaml.Controls.RadioButton platformView)
		{
			platformView.Checked -= OnPlatformChecked;
			platformView.Unchecked -= OnPlatformUnchecked;
			base.DisconnectHandler(platformView);
		}

		void OnPlatformChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
		{
			if (VirtualView is IRadioButton radioButton)
				radioButton.IsChecked = true;
		}

		void OnPlatformUnchecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
		{
			if (VirtualView is IRadioButton radioButton)
				radioButton.IsChecked = false;
		}

		public static partial void MapIsChecked(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;
			handler.PlatformView.IsChecked = virtualView.Selected?.CurrentValue ?? false;
		}

		public static partial void MapLabel(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;
			handler.PlatformView.Content = virtualView.Label?.CurrentValue ?? "";
		}

		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView)
		{
			// WinUI RadioButton uses theme foreground; custom color requires style override
		}
	}
}
