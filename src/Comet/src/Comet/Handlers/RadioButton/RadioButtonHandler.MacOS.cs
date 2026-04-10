using System;
using AppKit;
using Comet.MacOS;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler : ViewHandler<RadioButton, CUIRadioNSButton>
	{
		protected override CUIRadioNSButton CreatePlatformView()
		{
			var isChecked = VirtualView?.Selected?.CurrentValue ?? false;
			return new CUIRadioNSButton(isChecked);
		}

		public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			var platformView = PlatformView;
			if (platformView is null)
				return Size.Zero;

			var intrinsic = platformView.IntrinsicContentSize;
			var w = intrinsic.Width > 0 ? intrinsic.Width : 24;
			var h = intrinsic.Height > 0 ? intrinsic.Height : 24;

			return new Size(
				Math.Min(widthConstraint, w),
				Math.Min(heightConstraint, h));
		}

		protected override void ConnectHandler(CUIRadioNSButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.IsCheckedChanged += OnPlatformIsCheckedChanged;
		}

		protected override void DisconnectHandler(CUIRadioNSButton platformView)
		{
			platformView.IsCheckedChanged -= OnPlatformIsCheckedChanged;
			base.DisconnectHandler(platformView);
		}

		void OnPlatformIsCheckedChanged(object sender, EventArgs e)
		{
			if (VirtualView is IRadioButton radioButton)
			{
				radioButton.IsChecked = PlatformView.IsChecked;
			}
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
			handler.PlatformView.SetLabel(virtualView.Label?.CurrentValue ?? "");
			virtualView.InvalidateMeasurement();
		}

		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;

			var color = ((ITextStyle)virtualView).TextColor;
			if (color is not null)
			{
				var nsColor = NSColor.FromRgba(
					(nfloat)color.Red,
					(nfloat)color.Green,
					(nfloat)color.Blue,
					(nfloat)color.Alpha);
				handler.PlatformView.SetTextColor(nsColor);
			}
		}
	}
}
