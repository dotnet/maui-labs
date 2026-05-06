using System;
using Comet.iOS;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler : ViewHandler<RadioButton, CUIRadioButton>
	{
		protected override CUIRadioButton CreatePlatformView()
		{
			var isChecked = VirtualView?.Selected?.CurrentValue ?? false;
			return new CUIRadioButton(isChecked);
		}

		// Override to directly measure CUIRadioButton via SizeThatFits,
		// bypassing any IContentView.CrossPlatformMeasure routing that
		// returns Size.Zero because PresentedContent is null.
		public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			var platformView = PlatformView;
			if (platformView is null)
				return Size.Zero;

			var widthC = widthConstraint < 0 || double.IsInfinity(widthConstraint)
				? double.MaxValue : widthConstraint;
			var heightC = heightConstraint < 0 || double.IsInfinity(heightConstraint)
				? double.MaxValue : heightConstraint;

			var sizeThatFits = platformView.SizeThatFits(new CGSize(widthC, heightC));

			// Guarantee a minimum size so the radio circle is always visible
			var w = Math.Max(sizeThatFits.Width, 24);
			var h = Math.Max(sizeThatFits.Height, 24);

			return new Size(
				Math.Min(widthConstraint, w),
				Math.Min(heightConstraint, h));
		}

		protected override void ConnectHandler(CUIRadioButton platformView)
		{
			base.ConnectHandler(platformView);
			platformView.IsCheckedChanged += OnPlatformIsCheckedChanged;
		}

		protected override void DisconnectHandler(CUIRadioButton platformView)
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
			handler.PlatformView.SetTitle(virtualView.Label?.CurrentValue ?? "", UIControlState.Normal);
			virtualView.InvalidateMeasurement();
		}

		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView)
		{
			if (handler.PlatformView is null)
				return;

			var color = ((ITextStyle)virtualView).TextColor;
			if (color is not null)
			{
				handler.PlatformView.SetTitleColor(color.ToPlatform(), UIControlState.Normal);
			}
		}
	}
}
