using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AndroidRadioButton = global::Android.Widget.RadioButton;
using AndroidCompoundButton = global::Android.Widget.CompoundButton;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler : ViewHandler<RadioButton, AndroidRadioButton>
	{
		protected override AndroidRadioButton CreatePlatformView()
		{
			return new AndroidRadioButton(Context);
		}

		protected override void ConnectHandler(AndroidRadioButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.CheckedChange += OnPlatformCheckedChanged;
		}

		protected override void DisconnectHandler(AndroidRadioButton platformView)
		{
			platformView.CheckedChange -= OnPlatformCheckedChanged;
			base.DisconnectHandler(platformView);
		}

		void OnPlatformCheckedChanged(object sender, AndroidCompoundButton.CheckedChangeEventArgs e)
		{
			if (VirtualView is IRadioButton radioButton)
			{
				radioButton.IsChecked = e.IsChecked;
			}
		}

		public static partial void MapIsChecked(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;
			handler.PlatformView.Checked = virtualView.Selected?.CurrentValue ?? false;
		}

		public static partial void MapLabel(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;
			handler.PlatformView.Text = virtualView.Label?.CurrentValue ?? "";
		}

		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;
			var color = ((ITextStyle)virtualView).TextColor;
			if (color is not null)
			{
				handler.PlatformView.SetTextColor(color.ToPlatform());
			}
		}
	}
}
